namespace Tapedeck.Core.Models;

/// <summary>Row from the view <c>tracks_display</c>: the best available value for each field.</summary>
public sealed class TrackDisplay
{
    public long Id { get; init; }
    public string VideoId { get; init; } = "";
    public string Url { get; init; } = "";
    public string AddedAt { get; init; } = "";
    public string? Channel { get; init; }
    /// <summary>Upload date as yt-dlp reports it (<c>YYYYMMDD</c>), not the release date.</summary>
    public string? UploadDate { get; init; }

    public string Artist { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Album { get; init; }
    public string AlbumArtist { get; init; } = "";
    public int? Year { get; init; }
    /// <summary>Genres from the best available source, separated by <c>;</c>.</summary>
    public string? Genres { get; init; }
    public string? Cover { get; init; }

    // Origin of each value above, as stored in the view (see FieldSource)
    public string ArtistSource { get; init; } = "none";
    public string TitleSource { get; init; } = "none";
    public string AlbumSource { get; init; } = "none";
    public string AlbumArtistSource { get; init; } = "none";
    public string YearSource { get; init; } = "none";
    public string GenresSource { get; init; } = "none";

    /// <summary>The file exists and values changed after its tags were last written.</summary>
    public bool TagsStale { get; init; }

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

    public FieldSource ArtistFrom => FieldSourceExtensions.ParseDb(ArtistSource);
    public FieldSource TitleFrom => FieldSourceExtensions.ParseDb(TitleSource);
    public FieldSource AlbumFrom => FieldSourceExtensions.ParseDb(AlbumSource);
    public FieldSource AlbumArtistFrom => FieldSourceExtensions.ParseDb(AlbumArtistSource);
    public FieldSource YearFrom => FieldSourceExtensions.ParseDb(YearSource);
    public FieldSource GenresFrom => FieldSourceExtensions.ParseDb(GenresSource);

    public IReadOnlyList<string> GenreList =>
        string.IsNullOrEmpty(Genres) ? [] : Genres.Split(';', StringSplitOptions.RemoveEmptyEntries);
}
