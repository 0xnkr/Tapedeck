using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core.Models;
using Tapedeck.Core.Services;

namespace Tapedeck.Core.Spotify;

/// <param name="Searched">Songs searched in this run.</param>
/// <param name="Failed">Searches that failed for another reason than throttling; those songs stay unsearched.</param>
/// <param name="Throttled">YouTube asked to slow down, the run stopped early.</param>
public sealed record SpotifySearchSummary(int Searched, int Sure, int Unsure, int NotFound, int Failed, bool Throttled);

/// <summary>
/// Searches the songs on YouTube one by one and stores every result right away, so a stopped run continues where
/// it ended. Songs searched before are skipped. At most one yt-dlp call every 2 seconds for the whole app; when
/// YouTube throttles, the run stops instead of making it worse.
/// </summary>
public sealed class SpotifySearchService(
    IYtSearch search,
    ISpotifyMatchRepository matches,
    TimeSpan? minInterval = null,
    ILogger<SpotifySearchService>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    /// <summary>Enough for the album, the remaster and the music video to be among them.</summary>
    public const int RESULTS = 8;

    // the right song is nearly always among the first few; each fetch costs some seconds
    private const int MUSIC_RESULTS = 5;
    private const int MAX_FETCHES = 3;

    // a few failures can be a bad song; this many in a row means yt-dlp or the network is broken
    private const int MAX_FAILURES_IN_A_ROW = 3;

    private static readonly TimeSpan DEFAULT_INTERVAL = TimeSpan.FromSeconds(2);

    // shared by all instances: YouTube limits per app, not per service
    private static readonly SemaphoreSlim GATE = new(1, 1);
    private static DateTime LAST_SEARCH = DateTime.MinValue;

    /// <summary>The songs that still need a search, in the given order.</summary>
    /// <param name="again">Also the songs searched before, unless the user decided about them.</param>
    public async Task<IReadOnlyList<SpotifySong>> PendingAsync(IEnumerable<SpotifySong> songs, bool again = false)
    {
        var known = await matches.ListAsync();
        return songs.Where(s => !known.TryGetValue(s.Key, out var search) || (again && search.Decision is null)).ToList();
    }

    /// <param name="songs">The songs to search; ones with a stored search are skipped.</param>
    /// <param name="progress">Songs handled so far, out of the songs to search.</param>
    /// <param name="again">Searches songs searched before again, unless the user decided about them.</param>
    public async Task<SpotifySearchSummary> SearchAsync(IEnumerable<SpotifySong> songs,
        IProgress<(int Done, int Total)>? progress = null, bool again = false, CancellationToken ct = default)
    {
        var pending = await this.PendingAsync(songs, again);
        var states = new Dictionary<SpotifyMatchState, int>();
        int searched = 0, failed = 0, failuresInARow = 0;
        var throttled = false;
        progress?.Report((0, pending.Count));

        for (var i = 0; i < pending.Count; i++)
        {
            var song = pending[i];
            var query = SpotifyYtMatcher.MusicQuery(song);
            try
            {
                var results = await this.FindAsync(song, ct);
                await matches.SaveSearchAsync(song.Key, query, results);
                var state = SpotifyYtMatcher.Match(song, results).State;
                states[state] = states.GetValueOrDefault(state) + 1;
                searched++;
                failuresInARow = 0;
            }
            catch (YtThrottledException ex)
            {
                this.log.LogWarning("YouTube throttles the Spotify search after {Searched} of {Total} songs: {Error}", searched, pending.Count, ex.Message);
                throttled = true;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                // the song names stay out of the log: the history is private
                this.log.LogWarning("Spotify search {Number} of {Total} failed: {Error}", i + 1, pending.Count, ex.Message);
                if (++failuresInARow >= MAX_FAILURES_IN_A_ROW)
                {
                    this.log.LogWarning("Spotify search stopped after {Failures} failures in a row", failuresInARow);
                    break;
                }
            }
            progress?.Report((i + 1, pending.Count));
        }

        var summary = new SpotifySearchSummary(searched, states.GetValueOrDefault(SpotifyMatchState.Sure),
            states.GetValueOrDefault(SpotifyMatchState.Unsure), states.GetValueOrDefault(SpotifyMatchState.NotFound), failed, throttled);
        this.log.LogInformation("Spotify search: {Searched} of {Total} songs searched, {Sure} sure, {Unsure} unsure, {NotFound} not found, {Failed} failed",
            summary.Searched, pending.Count, summary.Sure, summary.Unsure, summary.NotFound, summary.Failed);
        return summary;
    }

    /// <summary>
    /// The YouTube Music song search finds the plain release first, but tells only the title; the songs with a
    /// fitting title are fetched until one is sure. Without a sure one, the plain YouTube search adds music videos
    /// and other uploads as suggestions.
    /// </summary>
    private async Task<IReadOnlyList<YtSearchResult>> FindAsync(SpotifySong song, CancellationToken ct)
    {
        var results = new List<YtSearchResult>();
        var songs = await this.ThrottledAsync(() => search.SearchMusicAsync(SpotifyYtMatcher.MusicQuery(song), MUSIC_RESULTS, ct), ct);
        foreach (var hit in songs.Where(s => SpotifyYtMatcher.TitleMatches(song, s.Title)).Take(MAX_FETCHES))
        {
            results.Add(await this.ThrottledAsync(() => search.FetchResultAsync(hit.VideoId, ct), ct));
            if (SpotifyYtMatcher.Match(song, results).State == SpotifyMatchState.Sure)
                return results;
        }

        var videos = await this.ThrottledAsync(() => search.SearchAsync(SpotifyYtMatcher.SearchQuery(song), RESULTS, ct), ct);
        results.AddRange(videos.Where(v => results.All(r => r.VideoId != v.VideoId)));
        return results;
    }

    private async Task<T> ThrottledAsync<T>(Func<Task<T>> call, CancellationToken ct)
    {
        await GATE.WaitAsync(ct);
        try
        {
            var wait = LAST_SEARCH + (minInterval ?? DEFAULT_INTERVAL) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);
            try
            {
                return await call();
            }
            finally
            {
                LAST_SEARCH = DateTime.UtcNow;
            }
        }
        finally
        {
            GATE.Release();
        }
    }
}
