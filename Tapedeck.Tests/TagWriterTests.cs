using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.MusicBrainz;
using Tapedeck.Core.Services;
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

    private string[] GainField()
    {
        using var file = TagLib.File.Create(Path.Combine(this.dir, "song.opus"));
        var xiph = (TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph);
        return xiph.GetField(TagWriter.TRACK_GAIN_FIELD);
    }
}
