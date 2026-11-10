using Microsoft.Data.Sqlite;
using Dapper;
using Tapedeck.Core;
using Tapedeck.Core.Lyrics;
using Tapedeck.Core.Models;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>
/// LRCLIB answers (real metadata, made-up lyric lines in <c>Fixtures/Lrclib/</c>), picking the fitting lyrics and
/// storing them next to the user's own.
/// </summary>
public sealed class LyricsTests : IAsyncLifetime
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), $"tapedeck-lyrics-{Guid.NewGuid():N}");
    private SqliteConnectionFactory factory = null!;
    private TrackRepository repo = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(this.dir);
        this.factory = new SqliteConnectionFactory(Path.Combine(this.dir, "tapedeck.db"));
        await new Migrator(this.factory).MigrateAsync();
        this.repo = new TrackRepository(this.factory);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.dir, recursive: true);
        return Task.CompletedTask;
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Lrclib", name));

    private static LyricsRequest Coolio(int? duration) => new(1, "Coolio", "Gangsta's Paradise (feat. L.V.)", null, duration);

    [Fact]
    public void Reads_a_record()
    {
        var record = LrclibJson.ParseRecord(Fixture("get-coolio.json"));

        Assert.Equal((3613370L, "Gangsta's Paradise", "Coolio", 241.0), (record.Id, record.Title, record.Artist, record.DurationS!.Value));
        Assert.StartsWith("[00:11.82]", record.Synced);
        Assert.False(record.Instrumental);
    }

    [Fact]
    public void The_album_length_gets_timed_lyrics()
    {
        var lyrics = LyricsMatcher.Pick(Coolio(241), LrclibJson.ParseSearch(Fixture("search-coolio.json")));

        Assert.NotNull(lyrics.Synced);
        Assert.NotNull(lyrics.Plain);
        Assert.False(lyrics.Instrumental);
    }

    [Fact]
    public void A_longer_music_video_gets_the_text_without_times()
    {
        // 270 s: an intro before the song; every timed line would come too early
        var lyrics = LyricsMatcher.Pick(Coolio(270), LrclibJson.ParseSearch(Fixture("search-coolio.json")));

        Assert.Null(lyrics.Synced);
        Assert.Equal("First line of the song\nSecond line of the song\nThird line of the song", lyrics.Plain);
    }

    [Fact]
    public void A_music_video_as_long_as_a_live_recording_gets_no_times_unless_it_is_the_release()
    {
        // the official video runs 256 s, exactly as long as a live recording; its timing belongs to the stage
        var records = LrclibJson.ParseSearch(Fixture("search-coolio.json"));

        Assert.Null(LyricsMatcher.Pick(Coolio(256), records).Synced);
        Assert.NotNull(LyricsMatcher.Pick(Coolio(256) with { IsTopic = true }, records).Synced);
    }

    [Fact]
    public async Task A_youtube_music_upload_is_settled_by_the_exact_lookup()
    {
        var id = await this.AddDownloadedAsync(241, "Coolio - Topic");
        var source = new FakeSource(LrclibJson.ParseRecord(Fixture("get-coolio.json")), []);

        await new LyricsService(this.repo, source).FetchMissingAsync();

        Assert.Equal(["get"], source.Calls);
        Assert.True((await this.repo.GetLyricsAsync(id)).IsSynced);
    }

    [Theory]
    [InlineData("Weird Al Yankovic", "Gangsta's Paradise")]
    [InlineData("Coolio", "Gangsta's Paradise (Live)")]
    [InlineData("Coolio", "Fantastic Voyage")]
    public void Other_artists_versions_and_songs_get_nothing(string artist, string title) =>
        Assert.True(LyricsMatcher.Pick(new LyricsRequest(1, artist, title, null, 241),
            LrclibJson.ParseSearch(Fixture("search-coolio.json"))).IsEmpty);

    [Fact]
    public void An_instrumental_is_known_without_text()
    {
        var lyrics = LyricsMatcher.Pick(new LyricsRequest(1, "Daft Punk", "Aerodynamic", null, 212),
            LrclibJson.ParseSearch(Fixture("search-instrumental.json")));

        Assert.Equal(new LrclibLyrics(null, null, Instrumental: true), lyrics);
    }

    [Fact]
    public void An_html_error_page_fails_only_this_answer()
    {
        Assert.Throws<InvalidDataException>(() => LrclibJson.ParseSearch("<html><body>502 Bad Gateway</body></html>"));
        Assert.Throws<InvalidDataException>(() => LrclibJson.ParseRecord("[]"));
    }

    [Fact]
    public void Lrc_text_without_times_and_tags()
    {
        const string LRC = "[ar:Coolio]\n[00:11.82] First line\n[00:14.66]Second line\n\n[01:02.5] Third line";

        Assert.True(LrcText.IsSynced(LRC));
        Assert.False(LrcText.IsSynced("Just a text\nwithout times"));
        Assert.Equal("First line\nSecond line\n\nThird line", LrcText.ToPlain(LRC));
        Assert.Equal(LrcText.OWNER_TAG + "\n" + LRC, LrcText.WithOwnerTag(LrcText.WithOwnerTag(LRC)));
    }

    [Fact]
    public void Lrc_lines_in_playing_order_with_repeats_pauses_and_offset()
    {
        const string LRC = "[re:Tapedeck]\n[ar:Coolio]\n[offset:+500]\n[00:10.5]First\n[00:20.00][00:40.00] Chorus\n[00:30.123]\nno time here";

        var lines = LrcText.Parse(LRC);

        Assert.Equal(
        [
            new LrcLine(TimeSpan.FromSeconds(10), "First"),
            new LrcLine(TimeSpan.FromSeconds(19.5), "Chorus"),
            new LrcLine(TimeSpan.FromMilliseconds(29_623), string.Empty),
            new LrcLine(TimeSpan.FromSeconds(39.5), "Chorus"),
        ], lines);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(10, 0)]
    [InlineData(19.4, 0)]
    [InlineData(19.5, 1)]
    [InlineData(35, 2)]
    [InlineData(500, 3)]
    public void The_line_sung_at_a_position(double seconds, int index)
    {
        var lines = LrcText.Parse("[offset:+500]\n[00:10.5]First\n[00:20.00][00:40.00] Chorus\n[00:30.123]");

        Assert.Equal(index, LrcText.IndexAt(lines, TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public async Task The_service_searches_when_the_exact_lookup_has_nothing_and_asks_each_track_once()
    {
        var id = await this.AddDownloadedAsync(270);
        var source = new FakeSource(exact: null, LrclibJson.ParseSearch(Fixture("search-coolio.json")));
        var service = new LyricsService(this.repo, source);

        var summary = await service.FetchMissingAsync();

        Assert.Equal((1, 1, 0), (summary.Asked, summary.Found, summary.Failed));
        Assert.Equal([id], summary.Changed);
        Assert.Equal(["get", "search"], source.Calls);
        var stored = await this.repo.GetLyricsAsync(id);
        Assert.Null(stored.Synced);
        Assert.False(stored.IsSynced);
        Assert.Equal(0, await service.CountPendingAsync());
    }

    [Fact]
    public async Task The_users_lyrics_win_and_mark_the_tags_outdated_only_when_they_change()
    {
        var id = await this.AddDownloadedAsync(241);
        await this.MarkWrittenLongAgoAsync(id);
        await this.repo.SaveLrclibLyricsAsync(id, new LrclibLyrics("Plain", "[00:01.00] Timed", false));
        Assert.True((await this.repo.GetDisplayAsync(id))!.TagsStale);
        Assert.Equal("[00:01.00] Timed", (await this.repo.GetLyricsAsync(id)).Text);

        await this.repo.SetUserLyricsAsync(id, "My own words");
        var lyrics = await this.repo.GetLyricsAsync(id);
        Assert.Equal(("My own words", false, true), (lyrics.Text, lyrics.IsSynced, lyrics.IsUser));

        // the same answer again changes nothing shown
        await this.repo.MarkTagsWrittenAsync(id);
        await this.repo.SaveLrclibLyricsAsync(id, new LrclibLyrics("Other", "[00:01.00] Other", false));
        Assert.False((await this.repo.GetDisplayAsync(id))!.TagsStale);

        await this.MarkWrittenLongAgoAsync(id);
        await this.repo.SetUserLyricsAsync(id, "  ");
        Assert.Equal("[00:01.00] Other", (await this.repo.GetLyricsAsync(id)).Text);
        Assert.True((await this.repo.GetDisplayAsync(id))!.TagsStale);
    }

    [Fact]
    public async Task Tracks_without_lyrics_are_asked_again_after_better_names_from_musicbrainz()
    {
        var id = await this.AddDownloadedAsync(241);
        await this.repo.SaveLrclibLyricsAsync(id, LrclibLyrics.NONE);
        Assert.Empty(await this.repo.ListWithoutLyricsAsync());

        await using (var connection = await this.factory.OpenAsync())
            await connection.ExecuteAsync("UPDATE tracks SET mb_checked_at = '2999-01-01T00:00:00.000Z' WHERE id = @id;", new { id });

        Assert.Equal([id], (await this.repo.ListWithoutLyricsAsync()).Select(r => r.Id));
    }

    // edits within the same millisecond as writing the tags would not count as newer
    private async Task MarkWrittenLongAgoAsync(long id)
    {
        await using var connection = await this.factory.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE tracks SET tags_written_at = '2000-01-01T00:00:00.000Z', edited_at = '2000-01-01T00:00:00.000Z' WHERE id = @id;",
            new { id });
    }

    private async Task<long> AddDownloadedAsync(int duration, string channel = "Tommy Boy")
    {
        var id = await this.repo.AddAsync("fPO76Jlnz6c", "https://www.youtube.com/watch?v=fPO76Jlnz6c");
        await this.repo.UpdateYtMetadataAsync(id, new YtMetadata("fPO76Jlnz6c", "Coolio - Gangsta's Paradise (feat. L.V.)", channel,
            "20091025", duration, null, null, null, null, "Coolio", "Gangsta's Paradise (feat. L.V.)", [], "{}"));
        await this.repo.UpdateDownloadStateAsync(id, DownloadStatus.Done, "song.opus", "opus");
        return id;
    }

    private sealed class FakeSource(LrclibRecord? exact, IReadOnlyList<LrclibRecord> found) : ILyricsSource
    {
        public List<string> Calls { get; } = [];

        public Task<LrclibRecord?> GetAsync(LyricsRequest request, CancellationToken ct = default)
        {
            this.Calls.Add("get");
            return Task.FromResult(exact);
        }

        public Task<IReadOnlyList<LrclibRecord>> SearchAsync(LyricsRequest request, CancellationToken ct = default)
        {
            this.Calls.Add("search");
            return Task.FromResult(found);
        }
    }
}
