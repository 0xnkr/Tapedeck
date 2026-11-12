using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tapedeck.Core.Spotify;

/// <summary>A track of a Spotify playlist as the embed page shows it.</summary>
/// <param name="Artists">All artists in one text, "P!nk, Nate Ruess"; a name with a comma cannot be told apart.</param>
public sealed record SpotifyPlaylistTrack(string Uri, string Title, string Artists, int DurationMs);

/// <param name="Owner">The user who made it, or "Spotify".</param>
/// <param name="TotalTracks">All tracks of the playlist; <c>null</c> when unknown. <see cref="Tracks"/> can be fewer.</param>
public sealed record SpotifyPlaylist(string Id, string Name, string? Owner, IReadOnlyList<SpotifyPlaylistTrack> Tracks, int? TotalTracks)
{
    /// <summary>The embed page shows no more without a login.</summary>
    public const int EMBED_LIMIT = 100;

    /// <summary>Spotify left out tracks: more than <see cref="EMBED_LIMIT"/>, or exactly that many and the total is unknown.</summary>
    public bool IsCut => this.TotalTracks is { } total ? total > this.Tracks.Count : this.Tracks.Count >= EMBED_LIMIT;
}

/// <summary>
/// Reads a public Spotify playlist without an account: the embed page carries its tracks as JSON, the first
/// <see cref="SpotifyPlaylist.EMBED_LIMIT"/> of them. The Web API is no way: it needs Premium and gives the tracks
/// only of playlists the user owns.
/// </summary>
public static partial class SpotifyPlaylistParser
{
    // the 22 base-62 characters of a Spotify ID
    [GeneratedRegex(@"(?:open\.spotify\.com/(?:intl-[\w-]+/)?(?:embed/)?playlist/|spotify:playlist:)([A-Za-z0-9]{22})(?![A-Za-z0-9])")]
    private static partial Regex PlaylistLink();

    [GeneratedRegex(@"<script id=""__NEXT_DATA__""[^>]*>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex NextData();

    // the normal playlist page carries its first tracks as base-64 JSON
    [GeneratedRegex(@"<script id=""initialState""[^>]*>([^<]+)</script>")]
    private static partial Regex InitialState();

    // the tracks of the playlist end with "pagingInfo":{"nextOffset":30},"totalCount":1447
    [GeneratedRegex(@"""pagingInfo"":\{[^{}]*\},""totalCount"":(\d+)")]
    private static partial Regex TotalCount();

    /// <summary>The playlist ID of a link ("https://open.spotify.com/playlist/…?si=…", "spotify:playlist:…").</summary>
    public static string? PlaylistId(string? text) =>
        text is not null && PlaylistLink().Match(text) is { Success: true } match ? match.Groups[1].Value : null;

    public static string EmbedUrl(string id) => $"https://open.spotify.com/embed/playlist/{id}";

    public static string PageUrl(string id) => $"https://open.spotify.com/playlist/{id}";

    /// <summary>The playlist from the HTML of the embed page; <c>null</c> when Spotify does not know it or it is private.</summary>
    /// <exception cref="FormatException">The page has no playlist data, e.g. because Spotify changed it.</exception>
    public static SpotifyPlaylist? ParseEmbed(string html, int? totalTracks = null)
    {
        var match = NextData().Match(html);
        if (!match.Success)
            throw new FormatException("The Spotify page contains no playlist data.");

        using var document = JsonDocument.Parse(match.Groups[1].Value);
        // an unknown or private playlist is a page "not found" with status 200
        if (TryGet(document.RootElement, out var status, "props", "pageProps", "status") && status.ValueKind == JsonValueKind.Number
            && status.GetInt32() == 404)
            return null;
        if (!TryGet(document.RootElement, out var entity, "props", "pageProps", "state", "data", "entity")
            || Text(entity, "type") != "playlist"
            || !entity.TryGetProperty("trackList", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new FormatException("The Spotify page contains no playlist data.");

        var tracks = new List<SpotifyPlaylistTrack>();
        foreach (var item in list.EnumerateArray())
        {
            // podcast episodes and local files have no song to search
            if (Text(item, "entityType") is { } type && type != "track")
                continue;
            var title = Text(item, "title");
            var artists = Text(item, "subtitle");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artists))
                continue;
            var duration = item.TryGetProperty("duration", out var ms) && ms.TryGetInt32(out var value) ? value : 0;
            tracks.Add(new SpotifyPlaylistTrack(Text(item, "uri") ?? string.Empty, title.Trim(), artists.Trim(), duration));
        }

        var name = Text(entity, "name") ?? Text(entity, "title") ?? string.Empty;
        return new SpotifyPlaylist(Text(entity, "id") ?? string.Empty, name.Trim(), Text(entity, "subtitle"), tracks, totalTracks);
    }

    /// <summary>The number of tracks from the state of the normal playlist page; <c>null</c> when missing.</summary>
    public static int? ParseTotalTracks(string html)
    {
        var state = InitialState().Match(html);
        if (!state.Success)
            return null;
        string json;
        try
        {
            json = Encoding.UTF8.GetString(Convert.FromBase64String(state.Groups[1].Value.Trim()));
        }
        catch (FormatException)
        {
            return null;
        }
        var match = TotalCount().Match(json);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : null;
    }

    /// <summary>
    /// The songs of the playlist for the YouTube search, in its order. A song that comes twice (the album and the
    /// single track) is taken once, at its first place.
    /// </summary>
    public static IReadOnlyList<SpotifySong> Songs(SpotifyPlaylist playlist)
    {
        var songs = new List<SpotifySong>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in playlist.Tracks)
        {
            var key = SpotifyAnalyzer.SongKey(track.Artists, track.Title);
            if (!seen.Add(key))
                continue;
            songs.Add(new SpotifySong(
                Key: key,
                Artist: track.Artists,
                Title: track.Title,
                Album: null,
                TrackUris: track.Uri.Length > 0 ? [track.Uri] : [],
                RealPlays: 0,
                Streams: 0,
                MsPlayed: 0,
                FirstPlayed: default,
                LastPlayed: default,
                RealPlaysPerYear: new Dictionary<int, int>(),
                EstimatedDurationMs: track.DurationMs > 0 ? track.DurationMs : null,
                Spellings: [$"{track.Artists} – {track.Title}"]));
        }
        return songs;
    }

    private static bool TryGet(JsonElement element, out JsonElement result, params string[] path)
    {
        result = element;
        foreach (var name in path)
        {
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(name, out result))
                return false;
        }
        return true;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
