using System.Globalization;
using System.Text;
using Tapedeck.Core.LastFm;
using Tapedeck.Core.MusicBrainz;

namespace Tapedeck.Core.Recommendations;

/// <summary>A Last.fm tag of an artist as stored, normalized by <see cref="RecommendationTags.NormalizeTag"/>.</summary>
/// <param name="Count">Last.fm's weight, 100 for the artist's strongest tag.</param>
public sealed record ArtistTag(string Tag, int Count);

/// <summary>What the Spotify streaming history says about a track.</summary>
/// <param name="Plays">Real plays after <see cref="Spotify.SpotifyPlayRules.DEFAULT"/>.</param>
/// <param name="EarlySkips">Plays ended with the forward button within the first 10 seconds.</param>
/// <param name="LastPlayed">The day (UTC) of the latest play or early skip.</param>
public sealed record ListeningHistory(int Plays, int EarlySkips, DateOnly? LastPlayed);

/// <summary>
/// The two Vorbis comments a phone player reads for its recommendations. Their format is a contract
/// with that player: <c>TAPEDECK_TAGS=synthwave=0.82;chill=0.40</c> and <c>TAPEDECK_HISTORY=plays=57;skips=3;last=2025-03-14</c>.
/// </summary>
public static class RecommendationTags
{
    /// <summary>The artist's Last.fm tags with their weight, strongest first.</summary>
    public const string TAGS_FIELD = "TAPEDECK_TAGS";

    /// <summary>Plays, early skips and the last day played from the Spotify streaming history.</summary>
    public const string HISTORY_FIELD = "TAPEDECK_HISTORY";

    /// <summary>Weaker tags (100 is the artist's strongest) are a few listeners' opinion.</summary>
    public const int MIN_COUNT = 10;

    public const int MAX_TAGS = 15;

    /// <summary>Tags that say something about the listener, not about the music.</summary>
    private static readonly HashSet<string> STOP_LIST = new(StringComparer.Ordinal)
    {
        "seen live", "seen in concert", "favorites", "favourites", "favorite", "favourite", "my favorite", "my favourite",
        "favorite artists", "favourite artists", "favorite bands", "favourite bands", "favorite songs", "favourite songs",
        "albums i own", "i own", "owned", "vinyl", "cd", "spotify", "check out", "to listen", "my music", "music",
        "love", "loved", "love it", "awesome", "amazing", "beautiful", "good", "cool", "best", "all",
        "under 2000 listeners",
    };

    /// <summary>The key artist tags are stored under: the shown artist after <see cref="MbNormalizer.Normalize"/>.</summary>
    public static string ArtistKey(string? artist) => MbNormalizer.Normalize(artist);

    /// <summary>
    /// Lowercase, single spaces, without <c>;</c> and <c>=</c> (they separate the entries in the file);
    /// <c>null</c> for an empty name.
    /// </summary>
    public static string? NormalizeTag(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Trim().ToLowerInvariant())
            builder.Append(c is ';' or '=' || char.IsWhiteSpace(c) || char.IsControl(c) ? ' ' : c);
        var normalized = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length == 0 ? null : normalized;
    }

    /// <summary>The strongest tags with some weight that are not on the stop list, at most <see cref="MAX_TAGS"/>.</summary>
    public static IReadOnlyList<ArtistTag> Select(IEnumerable<LastFmTag> tags) =>
        tags.Where(t => t.Count >= MIN_COUNT)
            .Select(t => (Name: NormalizeTag(t.Name), t.Count))
            .Where(t => t.Name is not null && !STOP_LIST.Contains(t.Name))
            .GroupBy(t => t.Name!, StringComparer.Ordinal)
            .Select(g => new ArtistTag(g.Key, g.Max(t => t.Count)))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Tag, StringComparer.Ordinal)
            .Take(MAX_TAGS)
            .ToList();

    /// <summary>
    /// <c>tag=weight;tag=weight</c> with the weight as count / 100 and two decimals, strongest first;
    /// <c>null</c> without tags, which removes the field.
    /// </summary>
    public static string? FormatTags(IEnumerable<ArtistTag> tags)
    {
        var entries = tags
            .Select(t => (Name: NormalizeTag(t.Tag), t.Count))
            .Where(t => t.Name is not null && t.Count > 0)
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Take(MAX_TAGS)
            .Select(t => $"{t.Name}={(Math.Min(t.Count, 100) / 100.0).ToString("0.00", CultureInfo.InvariantCulture)}")
            .ToList();
        return entries.Count == 0 ? null : string.Join(';', entries);
    }

    /// <summary><c>plays=57;skips=3;last=2025-03-14</c>; <c>null</c> without plays and skips, which removes the field.</summary>
    public static string? FormatHistory(ListeningHistory? history)
    {
        if (history is null || (history.Plays <= 0 && history.EarlySkips <= 0))
            return null;
        var text = string.Create(CultureInfo.InvariantCulture, $"plays={Math.Max(history.Plays, 0)};skips={Math.Max(history.EarlySkips, 0)}");
        return history.LastPlayed is { } last
            ? $"{text};last={last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            : text;
    }
}
