using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core.Models;

namespace Tapedeck.Core.Lyrics;

/// <param name="Asked">Tracks LRCLIB was asked for.</param>
/// <param name="Found">Of them, tracks with lyrics or known to be instrumental.</param>
/// <param name="Failed">Tracks left for the next run because the request failed.</param>
/// <param name="Changed">The tracks whose stored lyrics changed, to refresh their rows.</param>
public sealed record LyricsSummary(int Asked, int Found, int Failed, IReadOnlyList<long> Changed);

/// <summary>
/// Lyrics from LRCLIB for the downloaded tracks. The exact lookup comes first; without a fitting record, the
/// search finds uploads with other album names or lengths. The lyrics never reach the log, only the counts.
/// </summary>
public sealed class LyricsService(ITrackRepository tracks, ILyricsSource source, ILogger<LyricsService>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    // a few failures can be a bad name; this many in a row means the network or the service is down
    private const int MAX_FAILURES_IN_A_ROW = 3;

    /// <summary>How many tracks are waiting for LRCLIB.</summary>
    public async Task<int> CountPendingAsync() => (await tracks.ListWithoutLyricsAsync()).Count;

    /// <param name="progress">Tracks handled so far, out of the tracks waiting.</param>
    public async Task<LyricsSummary> FetchMissingAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var pending = await tracks.ListWithoutLyricsAsync();
        int asked = 0, found = 0, failed = 0, failuresInARow = 0;
        var changed = new List<long>();
        progress?.Report((0, pending.Count));

        for (var i = 0; i < pending.Count; i++)
        {
            try
            {
                var (lyrics, isChanged) = await this.FetchAndSaveAsync(pending[i], ct);
                asked++;
                if (!lyrics.IsEmpty)
                    found++;
                if (isChanged)
                    changed.Add(pending[i].Id);
                failuresInARow = 0;
            }
            // a timeout is a TaskCanceledException too, but no reason to stop the run
            catch (Exception ex) when ((ex is HttpRequestException or InvalidDataException or TaskCanceledException) && !ct.IsCancellationRequested)
            {
                failed++;
                this.log.LogWarning("LRCLIB lyrics for track {TrackId} failed: {Error}", pending[i].Id, ex.Message);
                if (++failuresInARow >= MAX_FAILURES_IN_A_ROW)
                {
                    this.log.LogWarning("LRCLIB lyrics stopped after {Failures} failures in a row", failuresInARow);
                    break;
                }
            }
            progress?.Report((i + 1, pending.Count));
        }

        this.log.LogInformation("LRCLIB lyrics: {Asked} of {Total} tracks asked, {Found} found, {Failed} failed",
            asked, pending.Count, found, failed);
        return new LyricsSummary(asked, found, failed, changed);
    }

    /// <summary>Asks LRCLIB again for one track, e.g. after the user corrected its title.</summary>
    /// <returns>What LRCLIB has now; <see cref="LrclibLyrics.NONE"/> for an unknown track.</returns>
    public async Task<LrclibLyrics> FetchAsync(long trackId, CancellationToken ct = default)
    {
        if (await tracks.GetLyricsRequestAsync(trackId) is not { } request || request.Artist.Length == 0 || request.Title.Length == 0)
            return LrclibLyrics.NONE;
        return (await this.FetchAndSaveAsync(request, ct)).Lyrics;
    }

    private async Task<(LrclibLyrics Lyrics, bool Changed)> FetchAndSaveAsync(LyricsRequest request, CancellationToken ct)
    {
        var lyrics = await this.FindAsync(request, ct);
        var before = await tracks.GetLyricsAsync(request.Id);
        await tracks.SaveLrclibLyricsAsync(request.Id, lyrics);
        this.log.LogInformation("LRCLIB for track {TrackId}: {Result}", request.Id,
            lyrics.Synced is not null ? "timed" : lyrics.Plain is not null ? "plain only" : lyrics.Instrumental ? "instrumental" : "nothing");
        var isChanged = before.Synced != lyrics.Synced || before.Plain != lyrics.Plain || before.Instrumental != lyrics.Instrumental;
        return (lyrics, isChanged);
    }

    /// <summary>
    /// A YouTube Music upload is settled by the exact lookup. Other videos need the search too: whether their
    /// length is the song's usual one shows only among all its records.
    /// </summary>
    private async Task<LrclibLyrics> FindAsync(LyricsRequest request, CancellationToken ct)
    {
        var exact = await source.GetAsync(request, ct);
        if (request.IsTopic && exact is not null && LyricsMatcher.Pick(request, [exact]) is { IsEmpty: false } picked)
            return picked;

        var found = await source.SearchAsync(request, ct);
        IReadOnlyList<LrclibRecord> records = exact is null || found.Any(r => r.Id == exact.Id) ? found : [exact, .. found];
        return LyricsMatcher.Pick(request, records);
    }
}
