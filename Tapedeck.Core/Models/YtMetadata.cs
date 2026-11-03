namespace Tapedeck.Core.Models;

/// <summary>Result of <c>yt-dlp -J</c>, mapped to the columns of <c>tracks</c>.</summary>
public sealed record YtMetadata(
    string VideoId,
    string? Title,
    string? Channel,
    string? UploadDate,
    int? DurationS,
    string? Track,
    string? Artist,
    string? Album,
    int? ReleaseYear,
    string? ParsedArtist,
    string? ParsedTitle,
    IReadOnlyList<string> Categories,
    string InfoJson);
