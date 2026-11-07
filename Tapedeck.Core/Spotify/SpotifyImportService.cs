using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core.Services;

namespace Tapedeck.Core.Spotify;

/// <summary>A song of the import: either already in the library or to be added from a YouTube video.</summary>
public sealed record SpotifyImportEntry(SpotifySong Song, long? TrackId, string? VideoId);

public sealed record SpotifyImportError(SpotifySong Song, string Message);

/// <param name="NewTracks">Tracks that were not in the library before.</param>
public sealed record SpotifyImportResult(long PlaylistId, int Added, int NewTracks, IReadOnlyList<SpotifyImportError> Errors);

/// <summary>
/// Adds the chosen videos of Spotify songs to the library and puts all songs of the import, including the ones
/// that were in the library before, into a new playlist in the order of the entries (most played first).
/// </summary>
public sealed class SpotifyImportService(
    ITrackRepository tracks,
    IPlaylistRepository playlists,
    ISpotifyMatchRepository matches,
    TrackImportService importer,
    ILogger<SpotifyImportService>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    // as for playlists: faster than one by one, without provoking YouTube's rate limit
    private const int PARALLEL_FETCHES = 3;

    /// <param name="playlistName">A free name; the caller checks it.</param>
    /// <param name="progress">Entries handled so far.</param>
    public async Task<SpotifyImportResult> ImportAsync(IReadOnlyList<SpotifyImportEntry> entries, string playlistName,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var known = (await tracks.ListDisplayAsync()).ToDictionary(t => t.VideoId, t => t.Id, StringComparer.Ordinal);
        foreach (var (videoId, trackId) in await tracks.ListAliasesAsync())
            known.TryAdd(videoId, trackId);

        // indexed by position, so the playlist keeps the order although fetches finish in any order
        var ids = new long?[entries.Count];
        var errors = new SpotifyImportError?[entries.Count];
        var newTracks = 0;
        var done = 0;
        progress?.Report((0, entries.Count));

        var options = new ParallelOptions { MaxDegreeOfParallelism = PARALLEL_FETCHES, CancellationToken = ct };
        await Parallel.ForEachAsync(Enumerable.Range(0, entries.Count), options, async (i, token) =>
        {
            var entry = entries[i];
            try
            {
                if (entry.TrackId is { } existing)
                {
                    ids[i] = existing;
                }
                else if (entry.VideoId is { } videoId)
                {
                    if (!known.TryGetValue(videoId, out var id))
                    {
                        id = await importer.AddAsync(VideoId.WatchUrl(videoId), token);
                        Interlocked.Increment(ref newTracks);
                    }
                    await matches.SetTrackAsync(entry.Song.Key, id, videoId);
                    ids[i] = id;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors[i] = new SpotifyImportError(entry.Song, ex.Message);
            }
            progress?.Report((Interlocked.Increment(ref done), entries.Count));
        });

        var playlistId = await playlists.CreateAsync(playlistName);
        // a song can come twice when two songs lead to the same track; the playlist keeps the first
        var added = await playlists.AddTracksAsync(playlistId, ids.OfType<long>().Distinct().ToList());
        var failed = errors.OfType<SpotifyImportError>().ToList();
        this.log.LogInformation("Spotify import: {Entries} songs, {Added} in playlist {PlaylistId}, {New} new in the library, {Failed} failed",
            entries.Count, added, playlistId, newTracks, failed.Count);
        foreach (var error in failed)
            this.log.LogWarning("Spotify song could not be added: {Error}", error.Message);
        return new SpotifyImportResult(playlistId, added, newTracks, failed);
    }
}
