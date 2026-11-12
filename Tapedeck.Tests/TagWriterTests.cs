using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.MusicBrainz;
using Tapedeck.Core.Lyrics;
using Tapedeck.Core.Recommendations;
using Tapedeck.Core.Services;
using Tapedeck.Core.Settings;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Writing tags into a real Opus file, with and without a MusicBrainz match.</summary>
public sealed class TagWriterTests : IAsyncLifetime
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), $"tapedeck-test-{Guid.NewGuid():N}");
    private TrackRepository repo = null!;
    private TagWriter writer = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(this.dir);
        var factory = new SqliteConnectionFactory(Path.Combine(this.dir, "tapedeck.db"));
        await new Migrator(factory).MigrateAsync();
        this.repo = new TrackRepository(factory);
        this.writer = new TagWriter(this.repo, new AppPaths(this.dir, this.dir));
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.dir, recursive: true);
        return Task.CompletedTask;
    }

    private async Task<long> AddWithFileAsync()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Audio", "tone-2s.opus"), Path.Combine(this.dir, "song.opus"));
        var id = await this.repo.AddAsync("aaaaaaaaaaa", "https://www.youtube.com/watch?v=aaaaaaaaaaa");
        await this.repo.UpdateYtMetadataAsync(id, new YtMetadata(
            "aaaaaaaaaaa", "Juicy J - Still Hustlin", "Channel", "20200101", 2, null, null, null, null,
            "Juicy J", "Still Hustlin", [], "{}"));
        await this.repo.UpdateDownloadStateAsync(id, DownloadStatus.Done, "song.opus", "opus");
        return id;
    }

    [Fact]
    public async Task Writes_corrections_into_a_file_without_musicbrainz_match()
    {
        var id = await this.AddWithFileAsync();
        await this.repo.SetUserFieldsAsync(id, new UserFields("Juicy J", "Still Hustlin", "Rubba Band Business", null, 2012, null));

        await this.writer.WriteTagsAsync(id);

        using var file = TagLib.File.Create(Path.Combine(this.dir, "song.opus"));
        Assert.Equal("Still Hustlin", file.Tag.Title);
        Assert.Equal("Rubba Band Business", file.Tag.Album);
        Assert.Equal(2012u, file.Tag.Year);
        Assert.True(string.IsNullOrEmpty(file.Tag.MusicBrainzTrackId));
    }

    [Fact]
    public async Task Only_the_main_genres_go_into_the_file()
    {
        var id = await this.AddWithFileAsync();
        await this.repo.SetUserTagsAsync(id, ["G-Funk", "West Coast Hip Hop", "Hip Hop"]);

        await this.writer.WriteTagsAsync(id);

        using var file = TagLib.File.Create(Path.Combine(this.dir, "song.opus"));
        Assert.Equal(["Hip-Hop"], file.Tag.Genres);
    }

    [Fact]
    public async Task Genres_without_a_main_genre_are_written_as_they_are()
    {
        var id = await this.AddWithFileAsync();
        await this.repo.SetUserTagsAsync(id, ["Hörspiel"]);

        await this.writer.WriteTagsAsync(id);

        using var file = TagLib.File.Create(Path.Combine(this.dir, "song.opus"));
        Assert.Equal(["Hörspiel"], file.Tag.Genres);
    }

    [Fact]
    public async Task Removing_a_match_clears_the_ids_in_the_file()
    {
        var id = await this.AddWithFileAsync();
        await this.repo.SaveMbResultAsync(id, MbMatchState.Matched,
            new MbPick("rec-1", "Still Hustlin", "Juicy J", "artist-1", "release-1", "group-1", "Album", "Juicy J", "2012", 3, 1, 2, 1),
            [], null, null);
        await this.writer.WriteTagsAsync(id);

        await this.repo.SaveMbResultAsync(id, MbMatchState.Rejected, null, [], null, null);
        await this.writer.WriteTagsAsync(id);

        using var file = TagLib.File.Create(Path.Combine(this.dir, "song.opus"));
        Assert.True(string.IsNullOrEmpty(file.Tag.MusicBrainzTrackId));
        Assert.Equal(0u, file.Tag.Track);
    }

    [Fact]
    public async Task Writes_the_track_gain_and_removes_it_without_a_measurement()
    {
        var id = await this.AddWithFileAsync();
        await this.repo.SetLoudnessAsync(id, -8);

        await this.writer.WriteTagsAsync(id);

        Assert.Equal(["-3840"], this.GainField());

        // a silent file has nothing to level out
        await this.repo.SetLoudnessAsync(id, -70);
        await this.writer.WriteTagsAsync(id);

        Assert.Empty(this.GainField());
    }

    [Fact]
    public async Task The_target_player_adds_replaygain_and_switching_back_removes_it()
    {
        var store = new SettingsStore(this.dir);
        var writer = new TagWriter(this.repo, new AppPaths(this.dir, this.dir), store);
        var id = await this.AddWithFileAsync();
        await this.repo.SetLoudnessAsync(id, -8);

        store.Update(s => s with { TargetPlayer = TargetPlayerCode.Of(TargetPlayer.PixelPlayer) });
        await writer.WriteTagsAsync(id);

        Assert.Equal(["-3840"], this.GainField());
        Assert.Equal(["-10.00 dB"], this.GainField(TagWriter.REPLAYGAIN_FIELD));

        store.Update(s => s with { TargetPlayer = TargetPlayerCode.Of(TargetPlayer.Standard) });
        Assert.Equal([id], await this.repo.MarkGainTagsOutdatedAsync());
        Assert.True((await this.repo.GetDisplayAsync(id))!.TagsStale);
        await writer.WriteTagsAsync(id);

        Assert.Equal(["-3840"], this.GainField());
        Assert.Empty(this.GainField(TagWriter.REPLAYGAIN_FIELD));
    }

    [Fact]
    public async Task Timed_lyrics_go_into_the_file_and_a_lrc_next_to_it()
    {
        var id = await this.AddWithFileAsync();
        var lrc = Path.Combine(this.dir, "song.lrc");
        await this.repo.SaveLrclibLyricsAsync(id, new LrclibLyrics("First line", "[00:01.00] First line", false));

        await this.writer.WriteTagsAsync(id);

        Assert.Equal(["[00:01.00] First line"], this.GainField(TagWriter.LYRICS_FIELD));
        Assert.Equal(LrcText.OWNER_TAG + "\r\n[00:01.00] First line", await File.ReadAllTextAsync(lrc));

        // without times, only the file gets them; the own .lrc goes
        await this.repo.SetUserLyricsAsync(id, "My own words\r\nSecond line");
        await this.writer.WriteTagsAsync(id);

        Assert.Equal(["My own words\nSecond line"], this.GainField(TagWriter.LYRICS_FIELD));
        Assert.False(File.Exists(lrc));

        // no lyrics at all removes the field
        await this.repo.SetUserLyricsAsync(id, null);
        await this.repo.SaveLrclibLyricsAsync(id, LrclibLyrics.NONE);
        await this.writer.WriteTagsAsync(id);

        Assert.Empty(this.GainField(TagWriter.LYRICS_FIELD));
    }

    [Fact]
    public async Task The_users_own_lrc_file_stays_as_it_is()
    {
        var id = await this.AddWithFileAsync();
        var lrc = Path.Combine(this.dir, "song.lrc");
        await File.WriteAllTextAsync(lrc, "[00:05.00] typed by hand");
        await this.repo.SaveLrclibLyricsAsync(id, new LrclibLyrics("Line", "[00:01.00] Line", false));

        await this.writer.WriteTagsAsync(id);
        Assert.Equal("[00:05.00] typed by hand", await File.ReadAllTextAsync(lrc));

        await this.repo.SaveLrclibLyricsAsync(id, LrclibLyrics.NONE);
        await this.writer.WriteTagsAsync(id);
        Assert.True(File.Exists(lrc));
    }

    [Theory]
    [InlineData(-8, "-10.00 dB")]
    [InlineData(-21.5, "+3.50 dB")]
    [InlineData(-18, "0.00 dB")]
    public void Replaygain_levels_to_minus_18_lufs(double lufs, string expected) =>
        Assert.Equal(expected, LoudnessMeter.ReplayGain(lufs));

    [Fact]
    public void Target_player_codes_round_trip_and_unknown_ones_are_the_standard()
    {
        foreach (var player in Enum.GetValues<TargetPlayer>())
            Assert.Equal(player, TargetPlayerCode.Parse(TargetPlayerCode.Of(player)));
        Assert.Equal(TargetPlayer.Standard, TargetPlayerCode.Parse("winamp"));
        Assert.False(PlayerQuirks.Of(TargetPlayer.Standard).ReplayGain);
    }

    [Fact]
    public async Task Writes_the_recommendation_tags_and_removes_them_without_data()
    {
        var id = await this.AddWithFileAsync();
        await this.repo.SaveArtistTagsAsync(RecommendationTags.ArtistKey("Juicy J"),
            [new ArtistTag("dirty south", 100), new ArtistTag("trap", 47)], [id]);
        await this.repo.SetListeningHistoryAsync(new Dictionary<long, ListeningHistory>
        {
            [id] = new(57, 3, new DateOnly(2025, 3, 14)),
        });

        await this.writer.WriteTagsAsync(id);

        Assert.Equal(["dirty south=1.00;trap=0.47"], this.GainField(RecommendationTags.TAGS_FIELD));
        Assert.Equal(["plays=57;skips=3;last=2025-03-14"], this.GainField(RecommendationTags.HISTORY_FIELD));

        await this.repo.SaveArtistTagsAsync(RecommendationTags.ArtistKey("Juicy J"), [], [id]);
        await this.repo.SetListeningHistoryAsync(new Dictionary<long, ListeningHistory>());
        await this.writer.WriteTagsAsync(id);

        Assert.Empty(this.GainField(RecommendationTags.TAGS_FIELD));
        Assert.Empty(this.GainField(RecommendationTags.HISTORY_FIELD));
    }

    private string[] GainField(string field = TagWriter.TRACK_GAIN_FIELD)
    {
        using var file = TagLib.File.Create(Path.Combine(this.dir, "song.opus"));
        var xiph = (TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph);
        return xiph.GetField(field);
    }
}
