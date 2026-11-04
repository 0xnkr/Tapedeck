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
public sealed class PlaylistImportService(ITrackRepository tracks, IPlaylistRepository playlists, TrackImportService importer)
{
    // a few yt-dlp calls at once: much faster than one by one, without provoking YouTube's rate limit
    private const int PARALLEL_FETCHES = 3;

    /// <param name="progress">Number of videos handled so far, out of the videos missing in the playlist.</param>
    public async Task<PlaylistImportResult> ImportAsync(long playlistId, YtPlaylist source,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var inPlaylist = (await playlists.GetTracksAsync(playlistId)).Select(t => t.VideoId).ToHashSet(StringComparer.Ordinal);
        var known = (await tracks.ListDisplayAsync()).ToDictionary(t => t.VideoId, t => t.Id, StringComparer.Ordinal);
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
        return new PlaylistImportResult(added, newTracks, errors.OfType<PlaylistImportError>().ToList());
    }
}
