using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Which tracks need a cover cut from their thumbnail, and how a new cover outdates the tags.</summary>
public sealed class CoverTests : IAsyncLifetime
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), $"tapedeck-covers-{Guid.NewGuid():N}");
    private TrackRepository repo = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(this.dir);
        var factory = new SqliteConnectionFactory(Path.Combine(this.dir, "tapedeck.db"));
        await new Migrator(factory).MigrateAsync();
        this.repo = new TrackRepository(factory);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.dir, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Tracks_with_thumbnail_but_without_cover_need_one()
    {
        var withThumb = await this.repo.AddAsync("dQw4w9WgXcQ", "dQw4w9WgXcQ");
        await this.repo.SetThumbPathAsync(withThumb, "thumbs/dQw4w9WgXcQ.jpg");
        var covered = await this.repo.AddAsync("9bZkp7q19f0", "9bZkp7q19f0");
        await this.repo.SetThumbPathAsync(covered, "thumbs/9bZkp7q19f0.jpg");
        await this.repo.SetCoverAsync(covered, "covers/release.jpg", "caa");
        await this.repo.AddAsync("kJQP7kiw5Fk", "kJQP7kiw5Fk");

        Assert.Equal([withThumb], await this.repo.ListWithoutCoverAsync());
    }

    [Fact]
    public async Task A_new_cover_outdates_the_tags_the_same_one_does_not()
    {
        var id = await this.repo.AddAsync("dQw4w9WgXcQ", "dQw4w9WgXcQ");
        await this.repo.UpdateDownloadStateAsync(id, DownloadStatus.Done, "A - One.opus", "opus");
        await this.repo.SetCoverAsync(id, "covers/yt-dQw4w9WgXcQ.jpg", "thumb");
        await this.repo.MarkTagsWrittenAsync(id);

        await this.repo.SetCoverAsync(id, "covers/yt-dQw4w9WgXcQ.jpg", "thumb");
        Assert.False((await this.repo.GetDisplayAsync(id))!.TagsStale);

        await this.repo.SetCoverAsync(id, "covers/release.jpg", "caa");
        var track = (await this.repo.GetDisplayAsync(id))!;
        Assert.True(track.TagsStale);
        Assert.Equal("covers/release.jpg", track.Cover);
    }
}
