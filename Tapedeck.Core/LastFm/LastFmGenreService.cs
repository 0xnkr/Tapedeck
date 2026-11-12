using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core.Genres;
using Tapedeck.Core.MusicBrainz;
using Tapedeck.Core.Recommendations;

namespace Tapedeck.Core.LastFm;

/// <summary>The artist tags of Last.fm; <see cref="LastFmClient"/> asks the web service.</summary>
public interface ILastFmTags
{
    /// <exception cref="LastFmException">An invalid key or another error than an unknown artist.</exception>
    Task<IReadOnlyList<LastFmTag>> GetArtistTagsAsync(string apiKey, string artist, CancellationToken ct = default);
}

/// <param name="Asked">Tracks Last.fm was asked for.</param>
/// <param name="WithGenres">Of them, tracks that got genres.</param>
/// <param name="Failed">Tracks and artists left for the next run because the request failed.</param>
/// <param name="InvalidKey">Last.fm refused the key; the run stopped.</param>
/// <param name="Tagged">The tracks whose file tags are outdated now (new genres or new artist tags), to refresh their rows.</param>
/// <param name="ArtistsAsked">Artists Last.fm was asked for their tags only, without a track waiting for genres.</param>
public sealed record LastFmSummary(int Asked, int WithGenres, int Failed, bool InvalidKey, IReadOnlyList<long> Tagged, int ArtistsAsked = 0);

/// <summary>
/// Genres for tracks MusicBrainz has none for, from the tags Last.fm's listeners gave the artist. Artist tags are
/// steadier than track tags and cover far more music. Only tags that map to a main genre become genres, which drops
/// noise like "seen live" or "favorites"; MusicBrainz and the user's own genres always win over them.
/// Every answer is also kept as the artist's tags (<see cref="RecommendationTags.Select"/>) for the files'
/// <c>TAPEDECK_TAGS</c>, and the other artists of the library without stored tags are asked for them, too.
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

    /// <summary>How many tracks are waiting for Last.fm, for genres or for their artist's tags.</summary>
    public async Task<int> CountPendingAsync()
    {
        var plan = await this.PlanAsync();
        var waiting = plan.GenreTracks.Select(t => t.Id).ToHashSet();
        foreach (var key in plan.ArtistsOnly)
            waiting.UnionWith(plan.TracksByArtist[key]);
        return waiting.Count;
    }

    /// <param name="progress">Tracks and artists handled so far, out of the ones waiting.</param>
    public async Task<LastFmSummary> FetchAsync(string apiKey, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var plan = await this.PlanAsync();
        var pending = plan.GenreTracks;
        var total = pending.Count + plan.ArtistsOnly.Count;
        // many songs share an artist: ask Last.fm once per artist and run
        var byArtist = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        int asked = 0, artistsAsked = 0, failed = 0, failuresInARow = 0, done = 0;
        var withGenres = new List<long>();
        var outdated = new HashSet<long>();
        progress?.Report((0, total));

        LastFmSummary Summary(bool invalidKey) =>
            new(asked, withGenres.Count, failed, invalidKey, outdated.Order().ToList(), artistsAsked);

        for (var i = 0; i < pending.Count; i++)
        {
            var (id, artist) = pending[i];
            try
            {
                if (!byArtist.TryGetValue(artist, out var genres))
                {
                    var lookup = await this.LookUpAsync(apiKey, artist, ct);
                    byArtist[artist] = genres = lookup.Genres;
                    await this.SaveArtistTagsAsync(plan, artist, lookup.Tags, outdated);
                }
                await tracks.SetLastFmTagsAsync(id, genres);
                asked++;
                if (genres.Count > 0)
                {
                    withGenres.Add(id);
                    outdated.Add(id);
                }
                failuresInARow = 0;
            }
            catch (LastFmException ex) when (ex.Code == LastFmException.INVALID_KEY)
            {
                this.log.LogWarning("Last.fm refused the API key: {Error}", ex.Message);
                return Summary(invalidKey: true);
            }
            catch (Exception ex) when (ex is LastFmException or HttpRequestException or InvalidDataException)
            {
                failed++;
                this.log.LogWarning("Last.fm genres for track {TrackId} failed: {Error}", id, ex.Message);
                if (++failuresInARow >= MAX_FAILURES_IN_A_ROW)
                {
                    this.log.LogWarning("Last.fm stopped after {Failures} failures in a row", failuresInARow);
                    return Summary(invalidKey: false);
                }
            }
            progress?.Report((++done, total));
        }

        // the artists of the other tracks, only for their tags in the files
        foreach (var key in plan.ArtistsOnly)
        {
            var artist = plan.ArtistNames[key];
            try
            {
                var lookup = await this.LookUpAsync(apiKey, artist, ct);
                await this.SaveArtistTagsAsync(plan, artist, lookup.Tags, outdated);
                artistsAsked++;
                failuresInARow = 0;
            }
            catch (LastFmException ex) when (ex.Code == LastFmException.INVALID_KEY)
            {
                this.log.LogWarning("Last.fm refused the API key: {Error}", ex.Message);
                return Summary(invalidKey: true);
            }
            catch (Exception ex) when (ex is LastFmException or HttpRequestException or InvalidDataException)
            {
                failed++;
                this.log.LogWarning("Last.fm tags of artist {Artist} failed: {Error}", artist, ex.Message);
                if (++failuresInARow >= MAX_FAILURES_IN_A_ROW)
                {
                    this.log.LogWarning("Last.fm stopped after {Failures} failures in a row", failuresInARow);
                    break;
                }
            }
            progress?.Report((++done, total));
        }

        this.log.LogInformation(
            "Last.fm: {Asked} of {Total} tracks asked for genres, {WithGenres} got genres, {Artists} more artists asked for tags, {Outdated} tracks with outdated tags, {Failed} failed",
            asked, pending.Count, withGenres.Count, artistsAsked, outdated.Count, failed);
        return Summary(invalidKey: false);
    }

    /// <summary>Stores the artist's tags once per run and collects the tracks whose files need them.</summary>
    private async Task SaveArtistTagsAsync(Plan plan, string artist, IReadOnlyList<ArtistTag> tags, HashSet<long> outdated)
    {
        var key = RecommendationTags.ArtistKey(artist);
        if (key.Length == 0 || !plan.Saved.Add(key))
            return;
        var trackIds = plan.TracksByArtist.GetValueOrDefault(key) ?? [];
        if (await tracks.SaveArtistTagsAsync(key, tags, trackIds))
            outdated.UnionWith(trackIds);
    }

    /// <summary>
    /// The tracks waiting for genres, and the artists of the library that Last.fm was not asked for yet and that are
    /// not asked for along with such a track anyway.
    /// </summary>
    private async Task<Plan> PlanAsync()
    {
        var genreTracks = await tracks.ListWithoutGenresAsync();
        var checkedKeys = await tracks.ListCheckedArtistKeysAsync();
        var tracksByArtist = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var track in (await tracks.ListDisplayAsync()).OrderBy(t => t.AddedAt, StringComparer.Ordinal).ThenBy(t => t.Id))
        {
            var key = RecommendationTags.ArtistKey(track.Artist);
            if (key.Length == 0)
                continue;
            if (!tracksByArtist.TryGetValue(key, out var ids))
            {
                tracksByArtist[key] = ids = [];
                names[key] = track.Artist;
            }
            ids.Add(track.Id);
        }

        var withGenreTrack = genreTracks.Select(t => RecommendationTags.ArtistKey(t.Artist)).ToHashSet(StringComparer.Ordinal);
        var artistsOnly = tracksByArtist.Keys.Where(k => !checkedKeys.Contains(k) && !withGenreTrack.Contains(k)).ToList();
        return new Plan(genreTracks, artistsOnly, names, tracksByArtist.ToDictionary(p => p.Key, p => (IReadOnlyList<long>)p.Value));
    }

    /// <summary>
    /// The genres and the tags of the artist as shown; for "A &amp; B" or "A feat. C" without tags of its own,
    /// the main artist's.
    /// </summary>
    private async Task<(IReadOnlyList<string> Genres, IReadOnlyList<ArtistTag> Tags)> LookUpAsync(string apiKey, string artist, CancellationToken ct)
    {
        var answer = await lastFm.GetArtistTagsAsync(apiKey, artist, ct);
        var genres = Pick(answer);
        var tags = RecommendationTags.Select(answer);
        var main = MbNormalizer.SplitArtists(MbNormalizer.SplitFeaturing(artist).Main).FirstOrDefault();
        if ((genres.Count == 0 || tags.Count == 0) && main is not null && !main.Equals(artist, StringComparison.OrdinalIgnoreCase))
        {
            var mainAnswer = await lastFm.GetArtistTagsAsync(apiKey, main, ct);
            if (genres.Count == 0)
                genres = Pick(mainAnswer);
            if (tags.Count == 0)
                tags = RecommendationTags.Select(mainAnswer);
        }
        return (genres, tags);
    }

    /// <param name="ArtistsOnly">Artist keys to ask for their tags only.</param>
    /// <param name="ArtistNames">The shown artist of the oldest track per key, the name Last.fm is asked for.</param>
    private sealed record Plan(
        IReadOnlyList<(long Id, string Artist)> GenreTracks,
        IReadOnlyList<string> ArtistsOnly,
        IReadOnlyDictionary<string, string> ArtistNames,
        IReadOnlyDictionary<string, IReadOnlyList<long>> TracksByArtist)
    {
        /// <summary>Artist keys whose tags were stored in this run.</summary>
        public HashSet<string> Saved { get; } = new(StringComparer.Ordinal);
    }
}
