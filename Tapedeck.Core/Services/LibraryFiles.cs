using Tapedeck.Core.Models;

namespace Tapedeck.Core.Services;

/// <summary>The audio file of a track in the library: is it still there, does its name fit, removing it.</summary>
public sealed class LibraryFiles(ITrackRepository repository, AppPaths paths)
{
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
        await repository.SetFilePathAsync(trackId, wanted);
        return wanted;
    }

    /// <summary>Removes a track from the library, its cached thumbnail and, if wanted, its audio file.</summary>
    public async Task RemoveAsync(long trackId, bool deleteFile)
    {
        if (await repository.GetDisplayAsync(trackId) is not { } track)
            return;

        if (deleteFile && track.FilePath is { } file)
            File.Delete(paths.ToLibraryPath(file));
        if (track.ThumbPath is { } thumb && thumb.StartsWith(paths.ThumbsDir, StringComparison.OrdinalIgnoreCase))
            File.Delete(thumb);

        await repository.DeleteAsync(trackId);
    }
}
