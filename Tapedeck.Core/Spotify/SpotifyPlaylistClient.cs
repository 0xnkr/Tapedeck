using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tapedeck.Core.Spotify;

/// <summary>Reads public Spotify playlists by their link, without an account; see <see cref="SpotifyPlaylistParser"/>.</summary>
public sealed class SpotifyPlaylistClient(HttpClient http, ILogger<SpotifyPlaylistClient>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    /// <exception cref="ArgumentException">The text is no playlist link.</exception>
    /// <exception cref="InvalidOperationException">The playlist does not exist or is private.</exception>
    /// <exception cref="FormatException">Spotify changed its page.</exception>
    public async Task<SpotifyPlaylist> FetchAsync(string link, CancellationToken ct = default)
    {
        var id = SpotifyPlaylistParser.PlaylistId(link) ?? throw new ArgumentException("No Spotify playlist link.", nameof(link));
        var embed = await this.GetAsync(SpotifyPlaylistParser.EmbedUrl(id), ct);
        var playlist = (embed is null ? null : SpotifyPlaylistParser.ParseEmbed(embed))
                       ?? throw new InvalidOperationException("The playlist does not exist or is private.");

        // only a full embed can be cut; the count is a bonus, the import works without it
        if (playlist.Tracks.Count >= SpotifyPlaylist.EMBED_LIMIT)
        {
            try
            {
                if (await this.GetAsync(SpotifyPlaylistParser.PageUrl(id), ct) is { } page)
                    playlist = playlist with { TotalTracks = SpotifyPlaylistParser.ParseTotalTracks(page) };
            }
            catch (HttpRequestException ex)
            {
                this.log.LogInformation("Spotify playlist page without track count: {Error}", ex.Message);
            }
        }

        this.log.LogInformation("Read Spotify playlist {Id}: {Tracks} tracks of {Total}", id, playlist.Tracks.Count,
            playlist.TotalTracks?.ToString() ?? "unknown");
        return playlist;
    }

    /// <returns>The page, or <c>null</c> for 404.</returns>
    private async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(UserAgent.PRODUCT);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
