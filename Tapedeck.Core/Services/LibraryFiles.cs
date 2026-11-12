using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core.Lyrics;
using Tapedeck.Core.Models;

namespace Tapedeck.Core.Services;

/// <summary>The audio file of a track in the library: is it still there, does its name fit, removing it.</summary>
public sealed class LibraryFiles(ITrackRepository repository, AppPaths paths, ILogger<LibraryFiles>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    /// <summary>
    /// The status to show: <see cref="DownloadStatus.Missing"/> when the track is done but its file was
    /// deleted or moved outside the app, otherwise the stored status.
    /// </summary>
    public DownloadStatus StatusOf(TrackDisplay track) =>
        track.DownloadStatus == DownloadStatus.Done
        && (track.FilePath is null || !File.Exists(paths.ToLibraryPath(track.FilePath)))
            ? DownloadStatus.Missing
            : track.DownloadStatus;

    /// <summary>
    /// Renames the file to <c>Artist - Title</c> after a correction (rules of <see cref="FileNaming.ChooseForTrack"/>).
    /// </summary>
    /// <returns>The new library-relative file name, or <c>null</c> when nothing changed.</returns>
    public async Task<string?> RenameToMatchAsync(long trackId)
    {
        if (await repository.GetDisplayAsync(trackId) is not { FilePath: { } current } track
            || !File.Exists(paths.ToLibraryPath(current)))
            return null;

        var extension = Path.GetExtension(current);
        var wanted = FileNaming.ChooseForTrack(current, track.Artist, track.Title, track.VideoId, extension,
            name => File.Exists(paths.ToLibraryPath(name)));
        if (string.Equals(wanted, current, StringComparison.Ordinal))
            return null;

        File.Move(paths.ToLibraryPath(current), paths.ToLibraryPath(wanted));
        LrcSidecar.MoveOwn(paths.ToLibraryPath(current), paths.ToLibraryPath(wanted));
        this.log.LogInformation("Renamed {From} to {To}", current, wanted);
        await repository.SetFilePathAsync(trackId, wanted);
        return wanted;
    }

    /// <summary>
    /// Keeps one track of a group of duplicates and removes the others, see <see cref="ITrackRepository.MergeIntoAsync"/>:
    /// their playlist places and rating go to the kept track, and adding their videos again leads to it.
    /// </summary>
    /// <param name="deleteFiles">Also delete the audio files of the removed tracks.</param>
    public async Task KeepAsync(long keepId, IReadOnlyList<long> duplicateIds, bool deleteFiles)
    {
        var others = duplicateIds.Where(id => id != keepId).ToList();
        await repository.MergeIntoAsync(keepId, others);
        this.log.LogInformation("Kept track {TrackId} of its duplicates and merged {Others} into it (files deleted: {DeleteFiles})",
            keepId, string.Join(", ", others), deleteFiles);
        foreach (var id in others)
            await this.RemoveAsync(id, deleteFiles);
    }

    /// <summary>Removes a track from the library, its cached thumbnail and, if wanted, its audio file.</summary>
    public async Task RemoveAsync(long trackId, bool deleteFile)
    {
        if (await repository.GetDisplayAsync(trackId) is not { } track)
            return;

        if (deleteFile && track.FilePath is { } file)
        {
            File.Delete(paths.ToLibraryPath(file));
            LrcSidecar.DeleteOwn(paths.ToLibraryPath(file));
        }
        if (track.ThumbPath is { } thumb && thumb.StartsWith(paths.ThumbsDir, StringComparison.OrdinalIgnoreCase))
            File.Delete(thumb);
        // the cover cut from the thumbnail is the track's own, even when a Cover Art Archive cover replaced it;
        // those are shared by the album and stay
        var cutCover = Path.Combine(paths.CoversDir, ThumbnailCoverService.FILE_PREFIX + track.VideoId + ".jpg");
        if (File.Exists(cutCover))
            File.Delete(cutCover);

        await repository.DeleteAsync(trackId);
        this.log.LogInformation("Removed track {TrackId} ({VideoId}, {Artist} - {Title}), file deleted: {Deleted}",
            trackId, track.VideoId, track.Artist, track.Title, deleteFile && track.FilePath is not null);
    }

    /// <summary>
    /// Removes several tracks one after another, like <see cref="RemoveAsync"/>. A track whose file cannot be deleted
    /// stays in the library and does not stop the others.
    /// </summary>
    /// <param name="deleteFiles">Also delete the audio files.</param>
    public async Task<RemoveManyResult> RemoveManyAsync(IReadOnlyList<long> trackIds, bool deleteFiles)
    {
        var removed = new List<long>();
        var failed = new List<RemoveFailure>();
        foreach (var id in trackIds.Distinct())
        {
            try
            {
                await this.RemoveAsync(id, deleteFiles);
                removed.Add(id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                this.log.LogWarning(ex, "Could not remove track {TrackId}", id);
                failed.Add(new RemoveFailure(id, ex.Message));
            }
        }
        this.log.LogInformation("Removed {Removed} of {Count} tracks (files deleted: {DeleteFiles}), {Failed} failed",
            removed.Count, removed.Count + failed.Count, deleteFiles, failed.Count);
        return new RemoveManyResult(removed, failed);
    }
}

/// <summary>A track that <see cref="LibraryFiles.RemoveManyAsync"/> could not remove, and why.</summary>
public sealed record RemoveFailure(long TrackId, string Message);

/// <summary>What <see cref="LibraryFiles.RemoveManyAsync"/> removed and what stayed.</summary>
public sealed record RemoveManyResult(IReadOnlyList<long> Removed, IReadOnlyList<RemoveFailure> Failed);
