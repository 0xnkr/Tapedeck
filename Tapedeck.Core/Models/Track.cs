namespace Tapedeck.Core.Models;

/// <summary>Raw row from <c>tracks</c>. Use <see cref="TrackDisplay"/> for display.</summary>
public sealed class Track
{
    public long Id { get; init; }
    public string VideoId { get; init; } = "";
    public string Url { get; init; } = "";
    public string AddedAt { get; init; } = "";

    public string? YtTitle { get; init; }
    public string? Channel { get; init; }
    public string? UploadDate { get; init; }
    public int? DurationS { get; init; }
    public string? YtTrack { get; init; }
    public string? YtArtist { get; init; }
    public string? YtAlbum { get; init; }
    public int? YtReleaseYear { get; init; }
    public string? ParsedArtist { get; init; }
    public string? ParsedTitle { get; init; }
    public string? InfoJson { get; init; }

    public string? MbRecordingId { get; init; }
    public string? MbArtist { get; init; }
    public string? MbTitle { get; init; }
    public string? MbAlbum { get; init; }
    public string? MbReleaseDate { get; init; }

    public string? UserArtist { get; init; }
    public string? UserTitle { get; init; }
    public string? UserAlbum { get; init; }
    public string? UserAlbumArtist { get; init; }
    public int? UserYear { get; init; }
    public int? Rating { get; init; }
    public string? Notes { get; init; }

    public string Status { get; init; } = "pending";
    public string? FilePath { get; init; }
    public string? FileFormat { get; init; }
    public string? DownloadedAt { get; init; }
    public string? ErrorMsg { get; init; }

    public string? ThumbPath { get; init; }
    public string? CoverPath { get; init; }
    public string? CoverSource { get; init; }
}
