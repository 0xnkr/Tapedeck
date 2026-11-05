namespace Tapedeck.Core.Models;

/// <summary>Raw row from <c>tracks</c>. Use <see cref="TrackDisplay"/> for display.</summary>
public sealed class Track
{
    public long Id { get; init; }
    public string VideoId { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string AddedAt { get; init; } = string.Empty;

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
    /// <summary>Database value of <see cref="MbState"/>.</summary>
    public string MbStatus { get; init; } = "none";
    public string? MbArtistId { get; init; }
    public string? MbReleaseId { get; init; }
    public string? MbReleaseGroupId { get; init; }
    public string? MbAlbumArtist { get; init; }
    public int? MbTrackNo { get; init; }
    public int? MbDiscNo { get; init; }
    public double? MbScore { get; init; }
    /// <summary>JSON of the best songs found (<see cref="MusicBrainz.MbCandidatesJson"/>).</summary>
    public string? MbCandidates { get; init; }
    public string? MbCheckedAt { get; init; }

    public MbMatchState MbState => MbMatchStateExtensions.ParseDb(this.MbStatus);

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
