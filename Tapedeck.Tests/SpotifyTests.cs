using System.IO.Compression;
using Tapedeck.Core.Models;
using Tapedeck.Core.Spotify;

namespace Tapedeck.Tests;

/// <summary>Reading Spotify's extended streaming history, counting real plays and choosing songs.</summary>
public sealed class SpotifyTests : IDisposable
{
    private static readonly string FIXTURES = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Spotify");

    private readonly string root = Path.Combine(Path.GetTempPath(), $"tapedeck-spotify-{Guid.NewGuid():N}");

    public SpotifyTests() => Directory.CreateDirectory(this.root);

    public void Dispose() => Directory.Delete(this.root, recursive: true);

    private static Task<SpotifyHistory> ReadFixturesAsync() => new SpotifyHistoryReader().ReadAsync(FIXTURES);

    private static SpotifyStream Play(string artist, string title, string date, int seconds = 200, string? end = "trackdone") =>
        new(DateTimeOffset.Parse(date + "T12:00:00Z"), $"spotify:track:{artist}-{title}", artist, title, null, seconds * 1000, "clickrow", end, false);

    [Fact]
    public async Task Reads_old_and_new_files_and_skips_what_is_no_song()
    {
        var history = await ReadFixturesAsync();

        Assert.Equal(new SpotifyReadStats(Files: 3, Rows: 14, Episodes: 1, NoMetadata: 1, Duplicates: 1), history.Stats);
        Assert.Equal(11, history.Streams.Count);
        Assert.Equal(DateTimeOffset.Parse("2018-09-01T10:00:00Z"), history.Streams[0].EndedAt);
        Assert.Equal(DateTimeOffset.Parse("2025-04-01T18:00:00Z"), history.Streams[^1].EndedAt);
        // the second device at the same moment is another play, not a duplicate
        Assert.Equal(2, history.Streams.Count(s => s.EndedAt == DateTimeOffset.Parse("2018-09-01T10:00:00Z")));
        Assert.Contains(history.Streams, s => s is { Title: "HUMBLE.", Incognito: true, Album: "DAMN." });
    }

    [Fact]
    public async Task Reads_the_zip_of_the_data_download()
    {
        var zipPath = Path.Combine(this.root, "my_spotify_data.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.GetFiles(FIXTURES))
                zip.CreateEntryFromFile(file, "Spotify Extended Streaming History/" + Path.GetFileName(file));
        }

        var history = await new SpotifyHistoryReader().ReadAsync(zipPath);

        Assert.Equal(11, history.Streams.Count);
        Assert.Equal(3, history.Stats.Files);
    }

    [Fact]
    public async Task Account_data_is_refused_with_a_hint()
    {
        await File.WriteAllTextAsync(Path.Combine(this.root, "StreamingHistory_music_0.json"),
            """[{"endTime":"2025-01-01 10:00","artistName":"Coolio","trackName":"Gangsta's Paradise","msPlayed":240000}]""");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new SpotifyHistoryReader().ReadAsync(this.root));
        Assert.Contains("extended streaming history", error.Message);
    }

    [Theory]
    [InlineData(30, "endplay", true)]
    [InlineData(29.9, "endplay", false)]
    [InlineData(200, "fwdbtn", false)]
    [InlineData(200, "backbtn", false)]
    [InlineData(200, "logout", true)]
    [InlineData(200, null, true)]
    public void Real_play_needs_30_seconds_and_no_skip_button(double seconds, string? end, bool real)
    {
        var stream = Play("Coolio", "Gangsta's Paradise", "2025-01-01", end: end) with { MsPlayed = (int)(seconds * 1000) };

        Assert.Equal(real, SpotifyPlayRules.DEFAULT.IsRealPlay(stream));
    }

    [Fact]
    public void Incognito_plays_can_be_left_out()
    {
        var stream = Play("Kendrick Lamar", "HUMBLE.", "2025-01-01") with { Incognito = true };

        Assert.True(SpotifyPlayRules.DEFAULT.IsRealPlay(stream));
        Assert.False((SpotifyPlayRules.DEFAULT with { CountIncognito = false }).IsRealPlay(stream));
    }

    [Theory]
    [InlineData("Coolio", "Gangsta's Paradise", "Coolio", "Gangsta's Paradise (feat. L.V.)")]
    [InlineData("Coolio", "Gangsta's Paradise", "Coolio", "Gangsta’s Paradise - Remastered 2011")]
    [InlineData("Kendrick Lamar", "All The Stars", "Kendrick Lamar, SZA", "All The Stars (with SZA)")]
    [InlineData("T.I.", "Live Your Life", "T.I.", "Live Your Life (feat. Rihanna)")]
    [InlineData("Avicii", "Levels", "Avicii", "Levels - Radio Edit")]
    [InlineData("Avicii", "Levels", "Avicii", "Levels - Single Version")]
    [InlineData("Kendrick Lamar", "Swimming Pools (Drank)", "Kendrick Lamar", "Swimming Pools (Drank) - Extended Version")]
    public void Versions_of_a_release_are_one_song(string artistA, string titleA, string artistB, string titleB) =>
        Assert.Equal(SpotifyAnalyzer.SongKey(artistA, titleA), SpotifyAnalyzer.SongKey(artistB, titleB));

    [Theory]
    [InlineData("Alright - Live")]
    [InlineData("Alright (Remix)")]
    [InlineData("Alright - Instrumental")]
    [InlineData("Alright - Club Mix")]
    [InlineData("Alright - Extended Mix")]
    [InlineData("Alright - World Tour 2018")]
    [InlineData("Alright - Stripped")]
    [InlineData("Alright - Japanese Version")]
    public void Other_recordings_are_other_songs(string title) =>
        Assert.NotEqual(SpotifyAnalyzer.SongKey("Kendrick Lamar", "Alright"), SpotifyAnalyzer.SongKey("Kendrick Lamar", title));

    [Fact]
    public void Each_remixer_makes_another_song()
    {
        Assert.NotEqual(SpotifyAnalyzer.SongKey("Elton John", "Cold Heart - PNAU Remix"), SpotifyAnalyzer.SongKey("Elton John", "Cold Heart - Claptone Remix"));
        Assert.Equal(SpotifyAnalyzer.SongKey("K.Flay", "Can't Sleep (Vanic Remix)"), SpotifyAnalyzer.SongKey("K.Flay", "Can't Sleep - Vanic Remix"));
        Assert.Equal(SpotifyAnalyzer.SongKey("Lil Nas X", "Old Town Road - Remix"), SpotifyAnalyzer.SongKey("Lil Nas X", "Old Town Road (feat. Billy Ray Cyrus) - Remix"));
    }

    [Fact]
    public async Task Songs_count_real_plays_across_their_tracks()
    {
        var songs = SpotifyAnalyzer.Analyze((await ReadFixturesAsync()).Streams);

        Assert.Equal(["Gangsta's Paradise", "Alright", "Alright - Live", "HUMBLE."], songs.Select(s => s.Title));
        var paradise = songs[0];
        Assert.Equal(3, paradise.RealPlays);
        Assert.Equal(6, paradise.Streams);
        Assert.Equal(834_900, paradise.MsPlayed);
        Assert.Equal(["spotify:track:gp-album", "spotify:track:gp-deluxe"], paradise.TrackUris);
        Assert.Equal("Gangsta's Paradise", paradise.Album);
        Assert.Equal(new Dictionary<int, int> { [2018] = 2, [2025] = 1 }, paradise.RealPlaysPerYear);
        Assert.Equal(240_000, paradise.EstimatedDurationMs);
        Assert.Equal(["Coolio – Gangsta's Paradise", "Coolio – Gangsta's Paradise (feat. L.V.)"], paradise.Spellings);
        Assert.Equal(DateTimeOffset.Parse("2018-09-01T10:00:00Z"), paradise.FirstPlayed);
        Assert.Equal(DateTimeOffset.Parse("2025-01-05T18:00:00Z"), paradise.LastPlayed);

        // played in the music video too, never to the end there
        var alright = songs[1];
        Assert.Equal(3, alright.RealPlays);
        Assert.Equal(219_000, alright.EstimatedDurationMs);
        Assert.Null(songs[2].EstimatedDurationMs);
    }

    [Fact]
    public async Task Selection_takes_top_songs_or_a_minimum_of_plays()
    {
        var streams = (await ReadFixturesAsync()).Streams;

        Assert.Equal(["Gangsta's Paradise"], new SpotifySelection(Top: 1).Apply(streams).Select(s => s.Title));
        Assert.Equal(["Gangsta's Paradise", "Alright"], new SpotifySelection(MinRealPlays: 2).Apply(streams).Select(s => s.Title));
        Assert.Equal(4, new SpotifySelection().Apply(streams).Count);
    }

    [Fact]
    public async Task Selection_counts_only_the_plays_of_its_period()
    {
        var streams = (await ReadFixturesAsync()).Streams;

        var year2018 = SpotifySelection.Year(2018).Apply(streams);
        Assert.Equal("Gangsta's Paradise", Assert.Single(year2018).Title);
        Assert.Equal(2, year2018[0].RealPlays);

        Assert.Equal(["Alright"], SpotifySelection.Year(2025, top: 1).Apply(streams).Select(s => s.Title));
        Assert.Empty(SpotifySelection.Year(2020).Apply(streams));
    }

    [Fact]
    public void Songs_without_a_real_play_are_never_selected()
    {
        var streams = new[] { Play("Coolio", "Gangsta's Paradise", "2025-01-01", seconds: 10) };

        Assert.Empty(new SpotifySelection().Apply(streams));
    }

    [Fact]
    public async Task Library_tracks_are_found_by_artist_and_title()
    {
        var songs = SpotifyAnalyzer.Analyze((await ReadFixturesAsync()).Streams);
        var library = SpotifyLibraryMatch.TracksBySongKey([
            new TrackDisplay { Id = 7, Artist = "Coolio", Title = "Gangsta's Paradise (feat. L.V.) [Official Music Video]" },
            new TrackDisplay { Id = 3, Artist = "Coolio", Title = "Gangsta's Paradise" },
            new TrackDisplay { Id = 9, Artist = "Kendrick Lamar", Title = "Alright" },
        ]);

        Assert.Equal(3, library[songs.Single(s => s.Title == "Gangsta's Paradise").Key]);
        Assert.Equal(9, library[songs.Single(s => s.Title == "Alright").Key]);
        Assert.False(library.ContainsKey(songs.Single(s => s.Title == "Alright - Live").Key));
    }

    [Fact]
    public async Task Report_shows_counts_and_library_marks()
    {
        var history = await ReadFixturesAsync();
        var songs = SpotifyAnalyzer.Analyze(history.Streams);
        var library = new Dictionary<string, long> { [songs[0].Key] = 3 };

        var report = SpotifyReport.ToMarkdown(history, library: library);

        Assert.Contains("- Skipped: 1 episodes, 1 rows without a track, 1 duplicates", report);
        Assert.Contains("| 1 | Coolio | Gangsta's Paradise | 3 | 6 |", report);
        Assert.Contains("| ✓ |", report);
        Assert.Contains("| ≥ 1 | 4 | 1 |", report);
        Assert.DoesNotContain("192.0.2.1", report);
    }
}
