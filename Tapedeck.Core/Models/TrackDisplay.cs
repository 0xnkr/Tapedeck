namespace Tapedeck.Core.Models;

/// <summary>Row from the view <c>tracks_display</c>: the best available value for each field.</summary>
public sealed class TrackDisplay
{
    public long Id { get; init; }
    public string VideoId { get; init; } = "";
    public string Url { get; init; } = "";
    public string AddedAt { get; init; } = "";

    public string Artist { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Album { get; init; }
    public string AlbumArtist { get; init; } = "";
    public int? Year { get; init; }
    /// <summary>Genres from the best available source, separated by <c>;</c>.</summary>
    public string? Genres { get; init; }
    public string? Cover { get; init; }

    public int? DurationS { get; init; }
    public int? Rating { get; init; }
    public string? Notes { get; init; }

    public string Status { get; init; } = "pending";
    public string? FilePath { get; init; }
    public string? FileFormat { get; init; }
    public string? DownloadedAt { get; init; }
    public string? ErrorMsg { get; init; }
    public string? ThumbPath { get; init; }

    public DownloadStatus DownloadStatus => DownloadStatusExtensions.ParseDb(Status);

    public IReadOnlyList<string> GenreList =>
        string.IsNullOrEmpty(Genres) ? [] : Genres.Split(';', StringSplitOptions.RemoveEmptyEntries);
}
