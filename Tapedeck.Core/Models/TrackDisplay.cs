namespace Tapedeck.Core.Models;

/// <summary>Row from the view <c>tracks_display</c>: the best available value for each field.</summary>
public sealed class TrackDisplay
{
    public long Id { get; init; }
    public string VideoId { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string AddedAt { get; init; } = string.Empty;
    public string? Channel { get; init; }
    /// <summary>Upload date as yt-dlp reports it (<c>YYYYMMDD</c>), not the release date.</summary>
    public string? UploadDate { get; init; }

    public string Artist { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Album { get; init; }
    public string AlbumArtist { get; init; } = string.Empty;
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

    public DownloadStatus DownloadStatus => DownloadStatusExtensions.ParseDb(this.Status);

    public FieldSource ArtistFrom => FieldSourceExtensions.ParseDb(this.ArtistSource);
    public FieldSource TitleFrom => FieldSourceExtensions.ParseDb(this.TitleSource);
    public FieldSource AlbumFrom => FieldSourceExtensions.ParseDb(this.AlbumSource);
    public FieldSource AlbumArtistFrom => FieldSourceExtensions.ParseDb(this.AlbumArtistSource);
    public FieldSource YearFrom => FieldSourceExtensions.ParseDb(this.YearSource);
    public FieldSource GenresFrom => FieldSourceExtensions.ParseDb(this.GenresSource);

    public IReadOnlyList<string> GenreList =>
        string.IsNullOrEmpty(this.Genres) ? [] : this.Genres.Split(';', StringSplitOptions.RemoveEmptyEntries);
}
