using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core.Models;

namespace Tapedeck.Core.Services;

/// <summary>A video of the YouTube playlist that could not be added.</summary>
public sealed record PlaylistImportError(string VideoId, string? Title, string Message);

/// <param name="Added">Tracks added to the playlist.</param>
/// <param name="NewTracks">Tracks that were not in the library before.</param>
public sealed record PlaylistImportResult(int Added, int NewTracks, IReadOnlyList<PlaylistImportError> Errors);

/// <summary>
/// Brings the videos of a YouTube playlist into a Tapedeck playlist. Used for the first import and for every sync:
/// videos that are not in the playlist yet are appended in YouTube order. Nothing is removed or reordered,
/// so the user's own changes to the playlist stay.
/// </summary>
public sealed class PlaylistImportService(
    ITrackRepository tracks,
    IPlaylistRepository playlists,
    TrackImportService importer,
    ILogger<PlaylistImportService>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    // a few yt-dlp calls at once: much faster than one by one, without provoking YouTube's rate limit
    private const int PARALLEL_FETCHES = 3;

    /// <param name="progress">Number of videos handled so far, out of the videos missing in the playlist.</param>
    public async Task<PlaylistImportResult> ImportAsync(long playlistId, YtPlaylist source,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var current = await playlists.GetTracksAsync(playlistId);
        var inPlaylist = current.Select(t => t.VideoId).ToHashSet(StringComparer.Ordinal);
        var known = (await tracks.ListDisplayAsync()).ToDictionary(t => t.VideoId, t => t.Id, StringComparer.Ordinal);
        // a duplicate merged away stays away: its video counts as the kept track
        var currentIds = current.Select(t => t.Id).ToHashSet();
        foreach (var (videoId, trackId) in await tracks.ListAliasesAsync())
        {
            known.TryAdd(videoId, trackId);
            if (currentIds.Contains(trackId))
                inPlaylist.Add(videoId);
        }
        var missing = source.Entries.Where(e => !inPlaylist.Contains(e.VideoId)).ToList();

        // indexed by position, so the playlist keeps the YouTube order although fetches finish in any order
        var ids = new long?[missing.Count];
        var errors = new PlaylistImportError?[missing.Count];
        var newTracks = 0;
        var done = 0;
        progress?.Report((0, missing.Count));

        var options = new ParallelOptions { MaxDegreeOfParallelism = PARALLEL_FETCHES, CancellationToken = ct };
        await Parallel.ForEachAsync(Enumerable.Range(0, missing.Count), options, async (i, token) =>
        {
            var entry = missing[i];
            if (known.TryGetValue(entry.VideoId, out var id))
            {
                ids[i] = id;
            }
            else
            {
                try
                {
                    ids[i] = await importer.AddAsync(VideoId.WatchUrl(entry.VideoId), token);
                    Interlocked.Increment(ref newTracks);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors[i] = new PlaylistImportError(entry.VideoId, entry.Title, ex.Message);
                }
            }
            progress?.Report((Interlocked.Increment(ref done), missing.Count));
        });

        var added = await playlists.AddTracksAsync(playlistId, ids.OfType<long>().ToList());
        var failed = errors.OfType<PlaylistImportError>().ToList();
        this.log.LogInformation("Playlist {ListId} ({Title}): {Videos} videos, {Added} added to playlist {PlaylistId}, {New} new in the library, {Failed} failed",
            source.Id, source.Title, source.Entries.Count, added, playlistId, newTracks, failed.Count);
        foreach (var error in failed)
            this.log.LogWarning("Video {VideoId} ({Title}) of playlist {ListId} could not be added: {Error}",
                error.VideoId, error.Title, source.Id, error.Message);
        return new PlaylistImportResult(added, newTracks, failed);
    }
}
