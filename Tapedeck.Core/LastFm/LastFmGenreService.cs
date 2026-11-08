using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core.Genres;
using Tapedeck.Core.MusicBrainz;

namespace Tapedeck.Core.LastFm;

/// <summary>The artist tags of Last.fm; <see cref="LastFmClient"/> asks the web service.</summary>
public interface ILastFmTags
{
    /// <exception cref="LastFmException">An invalid key or another error than an unknown artist.</exception>
    Task<IReadOnlyList<LastFmTag>> GetArtistTagsAsync(string apiKey, string artist, CancellationToken ct = default);
}

/// <param name="Asked">Tracks Last.fm was asked for.</param>
/// <param name="WithGenres">Of them, tracks that got genres.</param>
/// <param name="Failed">Tracks left for the next run because the request failed.</param>
/// <param name="InvalidKey">Last.fm refused the key; the run stopped.</param>
/// <param name="Tagged">The tracks that got genres, to refresh their rows.</param>
public sealed record LastFmSummary(int Asked, int WithGenres, int Failed, bool InvalidKey, IReadOnlyList<long> Tagged);

/// <summary>
/// Genres for tracks MusicBrainz has none for, from the tags Last.fm's listeners gave the artist. Artist tags are
/// steadier than track tags and cover far more music. Only tags that map to a main genre are kept, which drops
/// noise like "seen live" or "favorites"; MusicBrainz and the user's own genres always win over them.
/// </summary>
public sealed class LastFmGenreService(ITrackRepository tracks, ILastFmTags lastFm, ILogger<LastFmGenreService>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    /// <summary>Weaker tags (100 is the artist's strongest) are a few listeners' opinion.</summary>
    public const int MIN_COUNT = 10;

    public const int MAX_TAGS = 3;

    // a few failures can be a bad artist name; this many in a row means the network or the service is down
    private const int MAX_FAILURES_IN_A_ROW = 3;

    /// <summary>The strongest tags that map to a main genre, title-cased like the MusicBrainz genres.</summary>
    public static IReadOnlyList<string> Pick(IReadOnlyList<LastFmTag> tags) =>
        tags.Where(t => t.Count >= MIN_COUNT && GenreMap.Of(t.Name) is not null)
            .OrderByDescending(t => t.Count)
            .Select(t => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(t.Name.ToLowerInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MAX_TAGS)
            .ToList();

    /// <summary>How many tracks are waiting for Last.fm.</summary>
    public async Task<int> CountPendingAsync() => (await tracks.ListWithoutGenresAsync()).Count;

    /// <param name="progress">Tracks handled so far, out of the tracks waiting.</param>
    public async Task<LastFmSummary> FetchAsync(string apiKey, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var pending = await tracks.ListWithoutGenresAsync();
        // many songs share an artist: ask Last.fm once per artist and run
        var byArtist = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        int asked = 0, failed = 0, failuresInARow = 0;
        var tagged = new List<long>();
        progress?.Report((0, pending.Count));

        for (var i = 0; i < pending.Count; i++)
        {
            var (id, artist) = pending[i];
            try
            {
                if (!byArtist.TryGetValue(artist, out var genres))
                    byArtist[artist] = genres = await this.GenresOfAsync(apiKey, artist, ct);
                await tracks.SetLastFmTagsAsync(id, genres);
                asked++;
                if (genres.Count > 0)
                    tagged.Add(id);
                failuresInARow = 0;
            }
            catch (LastFmException ex) when (ex.Code == LastFmException.INVALID_KEY)
            {
                this.log.LogWarning("Last.fm refused the API key: {Error}", ex.Message);
                return new LastFmSummary(asked, tagged.Count, failed, InvalidKey: true, tagged);
            }
            catch (Exception ex) when (ex is LastFmException or HttpRequestException or InvalidDataException)
            {
                failed++;
                this.log.LogWarning("Last.fm genres for track {TrackId} failed: {Error}", id, ex.Message);
                if (++failuresInARow >= MAX_FAILURES_IN_A_ROW)
                {
                    this.log.LogWarning("Last.fm genres stopped after {Failures} failures in a row", failuresInARow);
                    break;
                }
            }
            progress?.Report((i + 1, pending.Count));
        }

        this.log.LogInformation("Last.fm genres: {Asked} of {Total} tracks asked, {WithGenres} got genres, {Failed} failed",
            asked, pending.Count, tagged.Count, failed);
        return new LastFmSummary(asked, tagged.Count, failed, InvalidKey: false, tagged);
    }

    /// <summary>The artist as shown; for "A & B" or "A feat. C" without tags of its own, the main artist.</summary>
    private async Task<IReadOnlyList<string>> GenresOfAsync(string apiKey, string artist, CancellationToken ct)
    {
        var genres = Pick(await lastFm.GetArtistTagsAsync(apiKey, artist, ct));
        var main = MbNormalizer.SplitArtists(MbNormalizer.SplitFeaturing(artist).Main).FirstOrDefault();
        if (genres.Count == 0 && main is not null && !main.Equals(artist, StringComparison.OrdinalIgnoreCase))
            genres = Pick(await lastFm.GetArtistTagsAsync(apiKey, main, ct));
        return genres;
    }
}
