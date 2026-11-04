using System.Globalization;
using Tapedeck.Core.Models;

namespace Tapedeck.Core.Csv;

/// <summary>One library row from a CSV file. Empty cells are <c>null</c> and leave the track unchanged.</summary>
public sealed record CsvTrack(
    int Line,
    string VideoId,
    string Url,
    string? Artist,
    string? Title,
    string? Album,
    string? AlbumArtist,
    int? Year,
    IReadOnlyList<string> Genres,
    int? Rating);

/// <summary>Why a CSV row was skipped.</summary>
public enum CsvProblem
{
    /// <summary>The file has no <c>url</c> column, nothing can be imported.</summary>
    MissingUrlColumn,
    /// <summary>The CSV syntax is broken from this line on.</summary>
    Malformed,
    InvalidUrl,
    InvalidYear,
    InvalidRating,
}

public sealed record CsvError(int Line, CsvProblem Problem, string? Value = null);

public sealed record CsvReadResult(IReadOnlyList<CsvTrack> Tracks, IReadOnlyList<CsvError> Errors);

/// <summary>What an imported row changes on a track; <c>null</c> parts stay as they are.</summary>
public sealed record TrackCorrections(UserFields? Fields, IReadOnlyList<string>? Genres, int? Rating)
{
    public bool IsEmpty => this.Fields is null && this.Genres is null && this.Rating is null;
}

/// <summary>
/// The library as CSV, for backups and moving to another computer:
/// <c>url, artist, title, album, album_artist, year, genres, rating</c>, genres separated by <c>;</c>.
/// The export holds the displayed values; on import only values that differ from YouTube become corrections.
/// </summary>
public static class LibraryCsv
{
    public static readonly IReadOnlyList<string> COLUMNS = ["url", "artist", "title", "album", "album_artist", "year", "genres", "rating"];

    private const char GENRE_SEPARATOR = ';';

    public static void Export(IEnumerable<TrackDisplay> tracks, TextWriter writer)
    {
        CsvFormat.WriteRecord(writer, COLUMNS);
        foreach (var t in tracks)
        {
            CsvFormat.WriteRecord(writer,
            [
                t.Url.Length > 0 ? t.Url : VideoId.WatchUrl(t.VideoId),
                t.Artist,
                t.Title,
                t.Album,
                t.AlbumArtist,
                t.Year?.ToString(CultureInfo.InvariantCulture),
                string.Join(GENRE_SEPARATOR, t.GenreList),
                t.Rating?.ToString(CultureInfo.InvariantCulture),
            ]);
        }
    }

    public static CsvReadResult Read(TextReader reader)
    {
        var tracks = new List<CsvTrack>();
        var errors = new List<CsvError>();
        Dictionary<string, int>? columns = null;

        try
        {
            foreach (var record in CsvFormat.Read(reader))
            {
                if (columns is null)
                {
                    columns = record.Fields
                        .Select((name, index) => (Name: name.Trim().ToLowerInvariant(), Index: index))
                        .GroupBy(c => c.Name)
                        .ToDictionary(g => g.Key, g => g.First().Index);
                    if (!columns.ContainsKey("url"))
                    {
                        errors.Add(new CsvError(record.Line, CsvProblem.MissingUrlColumn));
                        break;
                    }
                    continue;
                }

                if (ParseRow(record, columns, out var error) is { } track)
                    tracks.Add(track);
                else if (error is not null)
                    errors.Add(error);
            }
        }
        catch (CsvFormatException ex)
        {
            errors.Add(new CsvError(ex.Line, CsvProblem.Malformed));
        }

        return new CsvReadResult(tracks, errors);
    }

    /// <summary>
    /// Compares an imported row with the track as it is now (after YouTube metadata). Corrections the track
    /// already has are kept, values equal to the current ones are not turned into corrections.
    /// </summary>
    public static TrackCorrections Corrections(CsvTrack csv, TrackDisplay current)
    {
        var artist = Pick(csv.Artist, current.Artist, current.ArtistFrom);
        var title = Pick(csv.Title, current.Title, current.TitleFrom);
        var album = Pick(csv.Album, current.Album, current.AlbumFrom);

        // the album artist follows the artist unless it was set apart from it
        var effectiveArtist = artist ?? current.Artist;
        var albumArtist = csv.AlbumArtist is { } a && a == effectiveArtist && current.AlbumArtistFrom != FieldSource.User
            ? null
            : Pick(csv.AlbumArtist, current.AlbumArtist, current.AlbumArtistFrom);

        int? year = csv.Year is { } y && y != current.Year ? y
            : current.YearFrom == FieldSource.User ? current.Year
            : null;

        var changed = Differs(csv.Artist, current.Artist) || Differs(csv.Title, current.Title)
            || Differs(csv.Album, current.Album) || (Differs(csv.AlbumArtist, current.AlbumArtist) && albumArtist is not null)
            || (csv.Year is { } cy && cy != current.Year);
        var fields = changed ? new UserFields(artist, title, album, albumArtist, year, current.Notes) : null;

        var genres = csv.Genres.Count > 0
                     && !csv.Genres.Order(StringComparer.OrdinalIgnoreCase)
                         .SequenceEqual(current.GenreList.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
            ? csv.Genres
            : null;

        var rating = csv.Rating is { } r && r != current.Rating ? r : (int?)null;

        return new TrackCorrections(fields, genres, rating);
    }

    /// <summary>The user value for a field: the CSV value when it differs, else the existing correction, else none.</summary>
    private static string? Pick(string? csv, string? current, FieldSource currentSource) =>
        Differs(csv, current) ? csv
        : currentSource == FieldSource.User ? current
        : null;

    private static bool Differs(string? csv, string? current) => csv is not null && csv != (current ?? string.Empty);

    private static CsvTrack? ParseRow(CsvRecord record, Dictionary<string, int> columns, out CsvError? error)
    {
        error = null;
        string? Cell(string name) =>
            columns.TryGetValue(name, out var index) && index < record.Fields.Count && record.Fields[index].Trim() is { Length: > 0 } value
                ? value
                : null;

        var url = Cell("url");
        if (url is null && record.Fields.All(string.IsNullOrWhiteSpace))
            return null; // blank line
        if (!VideoId.TryParse(url, out var videoId))
        {
            error = new CsvError(record.Line, CsvProblem.InvalidUrl, url);
            return null;
        }

        int? year = null;
        if (Cell("year") is { } yearText)
        {
            if (!int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var y) || y is < 1000 or > 9999)
            {
                error = new CsvError(record.Line, CsvProblem.InvalidYear, yearText);
                return null;
            }
            year = y;
        }

        int? rating = null;
        if (Cell("rating") is { } ratingText)
        {
            if (!int.TryParse(ratingText, NumberStyles.None, CultureInfo.InvariantCulture, out var r) || r is < 1 or > 5)
            {
                error = new CsvError(record.Line, CsvProblem.InvalidRating, ratingText);
                return null;
            }
            rating = r;
        }

        var genres = (Cell("genres") ?? string.Empty)
            .Split(GENRE_SEPARATOR, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CsvTrack(record.Line, videoId, url!, Cell("artist"), Cell("title"), Cell("album"),
            Cell("album_artist"), year, genres, rating);
    }
}
