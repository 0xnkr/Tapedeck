namespace Tapedeck.Core.Models;

/// <summary>Values of the column <c>tracks.status</c>, stored as lowercase text.</summary>
public enum DownloadStatus
{
    Pending,
    Downloading,
    Done,
    Error,
}

public static class DownloadStatusExtensions
{
    public static string ToDb(this DownloadStatus status) => status.ToString().ToLowerInvariant();

    public static DownloadStatus ParseDb(string value) => Enum.Parse<DownloadStatus>(value, ignoreCase: true);
}
