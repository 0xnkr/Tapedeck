namespace Tapedeck.Core.Models;

/// <summary>Values of the column <c>tracks.status</c>, stored as lowercase text.</summary>
public enum DownloadStatus
{
    Pending,
    Downloading,
    Done,
    Error,
    /// <summary>
    /// Display only, never stored: the track is <see cref="Done"/> but its file is gone
    /// (see <see cref="Services.LibraryFiles.StatusOf"/>).
    /// </summary>
    Missing,
}

public static class DownloadStatusExtensions
{
    public static string ToDb(this DownloadStatus status) => status == DownloadStatus.Missing
        ? throw new ArgumentException("Missing is derived from the file system and cannot be stored.", nameof(status))
        : status.ToString().ToLowerInvariant();

    public static DownloadStatus ParseDb(string value) => Enum.Parse<DownloadStatus>(value, ignoreCase: true);
}
