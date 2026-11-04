using Tapedeck.Core.Csv;
using Tapedeck.Core.Models;

namespace Tapedeck.Tests;

public sealed class CsvTests
{
    private const string URL = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    private static TrackDisplay Display(string artist = "Rick Astley", string title = "Never Gonna Give You Up",
        string? album = null, int? year = null, string? genres = null, int? rating = null,
        string artistSource = "yt", string albumSource = "none", string yearSource = "none") => new()
    {
        Id = 1, VideoId = "dQw4w9WgXcQ", Url = URL, Artist = artist, Title = title, Album = album, AlbumArtist = artist,
        Year = year, Genres = genres, Rating = rating, ArtistSource = artistSource, TitleSource = "yt",
        AlbumSource = albumSource, AlbumArtistSource = "derived", YearSource = yearSource,
    };

    private static CsvTrack Row(string? artist = null, string? title = null, string? album = null, string? albumArtist = null,
        int? year = null, string[]? genres = null, int? rating = null) =>
        new(2, "dQw4w9WgXcQ", URL, artist, title, album, albumArtist, year, genres ?? [], rating);

    [Fact]
    public void Format_roundtrips_commas_quotes_line_breaks_and_umlauts()
    {
        string[] values = ["plain", "with, comma", "say \"hi\"", "two\r\nlines", "Ärger über Öl", string.Empty];
        var writer = new StringWriter();
        CsvFormat.WriteRecord(writer, values);
        CsvFormat.WriteRecord(writer, ["second"]);

        var records = CsvFormat.Read(new StringReader(writer.ToString())).ToList();

        Assert.Equal(2, records.Count);
        Assert.Equal(values.Select(v => v.Replace("\r\n", "\n")), records[0].Fields.Select(f => f.Replace("\r\n", "\n")));
        Assert.Equal(1, records[0].Line);
        Assert.Equal(3, records[1].Line);
    }

    [Fact]
    public void Format_reports_the_line_of_broken_syntax()
    {
        var ex = Assert.Throws<CsvFormatException>(() => CsvFormat.Read(new StringReader("a,b\nc,\"open")).ToList());
        Assert.Equal(2, ex.Line);
    }

    [Fact]
    public void Export_writes_header_and_display_values()
    {
        var writer = new StringWriter();
        LibraryCsv.Export([Display(album: "Whenever You Need Somebody", year: 1987, genres: "Pop;Dance", rating: 5)], writer);

        var lines = writer.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("url,artist,title,album,album_artist,year,genres,rating", lines[0]);
        Assert.Equal($"{URL},Rick Astley,Never Gonna Give You Up,Whenever You Need Somebody,Rick Astley,1987,Pop;Dance,5", lines[1]);
    }

    [Fact]
    public void Read_accepts_any_column_order_and_reports_bad_rows()
    {
        const string CSV = """
            title,url,rating,year
            Song,https://youtu.be/dQw4w9WgXcQ,4,2001

            Bad link,https://example.com,,
            Bad rating,https://youtu.be/9bZkp7q19f0,9,
            """;

        var result = LibraryCsv.Read(new StringReader(CSV));

        var track = Assert.Single(result.Tracks);
        Assert.Equal(("dQw4w9WgXcQ", "Song", 4, 2001), (track.VideoId, track.Title, track.Rating, track.Year));
        Assert.Equal([CsvProblem.InvalidUrl, CsvProblem.InvalidRating], result.Errors.Select(e => e.Problem));
        Assert.Equal([4, 5], result.Errors.Select(e => e.Line));
    }

    [Fact]
    public void Read_requires_a_url_column()
    {
        var result = LibraryCsv.Read(new StringReader("title,artist\nSong,Someone"));
        Assert.Empty(result.Tracks);
        Assert.Equal(CsvProblem.MissingUrlColumn, Assert.Single(result.Errors).Problem);
    }

    [Fact]
    public void Same_values_as_youtube_are_no_corrections()
    {
        var corrections = LibraryCsv.Corrections(Row("Rick Astley", "Never Gonna Give You Up", albumArtist: "Rick Astley"), Display());
        Assert.True(corrections.IsEmpty);
    }

    [Fact]
    public void Only_differing_values_become_corrections()
    {
        var corrections = LibraryCsv.Corrections(
            Row("Rick", "Never Gonna Give You Up", "Whenever You Need Somebody", "Rick", 1987, ["Pop"], 4),
            Display(genres: "Dance"));

        Assert.Equal(new UserFields("Rick", null, "Whenever You Need Somebody", null, 1987, null), corrections.Fields);
        Assert.Equal(["Pop"], corrections.Genres!);
        Assert.Equal(4, corrections.Rating);
    }

    [Fact]
    public void Existing_corrections_are_kept()
    {
        var current = Display(artist: "Rick", album: "Own Album", year: 1990, artistSource: "user", albumSource: "user", yearSource: "user");
        var corrections = LibraryCsv.Corrections(Row(title: "New Title"), current);

        Assert.Equal(new UserFields("Rick", "New Title", "Own Album", null, 1990, null), corrections.Fields);
    }
}
