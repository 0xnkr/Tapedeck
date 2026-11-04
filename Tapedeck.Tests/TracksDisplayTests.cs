using Microsoft.Data.Sqlite;
using Tapedeck.Core.Models;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Checks the best-value selection in <c>tracks_display</c> on a freshly migrated database.</summary>
public sealed class TracksDisplayTests : IAsyncLifetime
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"tapedeck-test-{Guid.NewGuid():N}.db");
    private TrackRepository repo = null!;

    public async Task InitializeAsync()
    {
        var factory = new SqliteConnectionFactory(this.dbPath);
        Assert.Equal(Migrator.LatestVersion, await new Migrator(factory).MigrateAsync());
        this.repo = new TrackRepository(factory);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            File.Delete(file);
        return Task.CompletedTask;
    }

    private static YtMetadata Meta(string? artist = null, string? track = null) => new(
        "dQw4w9WgXcQ", "Rick Astley - Never Gonna Give You Up (Official Video)", "RickAstleyVEVO",
        "20091025", 213, track, artist, null, null, "Rick Astley", "Never Gonna Give You Up", [], "{}");

    [Fact]
    public async Task Duplicate_video_id_returns_same_track()
    {
        var first = await this.repo.AddAsync("dQw4w9WgXcQ", "https://youtu.be/dQw4w9WgXcQ");
        var second = await this.repo.AddAsync("dQw4w9WgXcQ", "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Prefers_user_over_yt_over_parsed()
    {
        var id = await this.repo.AddAsync("dQw4w9WgXcQ", "u");

        await this.repo.UpdateYtMetadataAsync(id, Meta());
        var display = await this.repo.GetDisplayAsync(id);
        Assert.Equal("Rick Astley", display!.Artist);
        Assert.Equal("Never Gonna Give You Up", display.Title);
        Assert.Equal("Rick Astley", display.AlbumArtist);

        await this.repo.UpdateYtMetadataAsync(id, Meta(artist: "Rick Astley (yt)", track: "NGGYU (yt)"));
        display = await this.repo.GetDisplayAsync(id);
        Assert.Equal("Rick Astley (yt)", display!.Artist);
        Assert.Equal("NGGYU (yt)", display.Title);

        await this.repo.SetUserFieldsAsync(id, new UserFields("Rick", null, "Whenever You Need Somebody", "Various", 1987, null));
        display = await this.repo.GetDisplayAsync(id);
        Assert.Equal("Rick", display!.Artist);
        Assert.Equal("NGGYU (yt)", display.Title);
        Assert.Equal("Whenever You Need Somebody", display.Album);
        Assert.Equal("Various", display.AlbumArtist);
        Assert.Equal(1987, display.Year);
    }

    [Fact]
    public async Task Genres_come_from_best_source_only()
    {
        var id = await this.repo.AddAsync("dQw4w9WgXcQ", "u");
        await this.repo.AddTagAsync(id, "Entertainment", TagSource.Yt);
        Assert.Equal(["Entertainment"], (await this.repo.GetDisplayAsync(id))!.GenreList);

        await this.repo.AddTagAsync(id, "Pop", TagSource.Lastfm);
        await this.repo.AddTagAsync(id, "Dance", TagSource.Lastfm);
        Assert.Equal(["Dance", "Pop"], (await this.repo.GetDisplayAsync(id))!.GenreList);

        await this.repo.AddTagAsync(id, "Synth-Pop", TagSource.User);
        Assert.Equal(["Synth-Pop"], (await this.repo.GetDisplayAsync(id))!.GenreList);
    }

    [Fact]
    public async Task Cover_falls_back_to_thumbnail()
    {
        var id = await this.repo.AddAsync("dQw4w9WgXcQ", "u");
        await this.repo.SetThumbPathAsync(id, "thumb.jpg");
        Assert.Equal("thumb.jpg", (await this.repo.GetDisplayAsync(id))!.Cover);

        await this.repo.SetCoverAsync(id, "cover.jpg", "caa");
        Assert.Equal("cover.jpg", (await this.repo.GetDisplayAsync(id))!.Cover);
    }

    [Fact]
    public async Task Sources_follow_the_best_value()
    {
        var id = await this.repo.AddAsync("dQw4w9WgXcQ", "u");
        var display = await this.repo.GetDisplayAsync(id);
        Assert.Equal(FieldSource.None, display!.ArtistFrom);
        Assert.Equal(FieldSource.None, display.TitleFrom);

        await this.repo.UpdateYtMetadataAsync(id, Meta());
        display = await this.repo.GetDisplayAsync(id);
        Assert.Equal(FieldSource.Parsed, display!.ArtistFrom);
        Assert.Equal(FieldSource.Parsed, display.TitleFrom);
        Assert.Equal(FieldSource.Derived, display.AlbumArtistFrom);
        Assert.Equal(FieldSource.None, display.AlbumFrom);
        Assert.Equal(FieldSource.None, display.YearFrom);
        Assert.Equal("RickAstleyVEVO", display.Channel);
        Assert.Equal("20091025", display.UploadDate);

        await this.repo.UpdateYtMetadataAsync(id, Meta(artist: "Rick Astley", track: "NGGYU"));
        await this.repo.SetUserFieldsAsync(id, new UserFields(null, null, "Whenever You Need Somebody", "Various", 1987, null));
        display = await this.repo.GetDisplayAsync(id);
        Assert.Equal(FieldSource.Yt, display!.ArtistFrom);
        Assert.Equal(FieldSource.Yt, display.TitleFrom);
        Assert.Equal(FieldSource.User, display.AlbumFrom);
        Assert.Equal(FieldSource.User, display.AlbumArtistFrom);
        Assert.Equal(FieldSource.User, display.YearFrom);
    }

    [Fact]
    public async Task Artist_falls_back_to_channel()
    {
        var id = await this.repo.AddAsync("dQw4w9WgXcQ", "u");
        await this.repo.UpdateYtMetadataAsync(id, new YtMetadata(
            "dQw4w9WgXcQ", "Untitled upload", "Some Channel", "20240101", 100, null, null, null, null, null, null, [], "{}"));

        var display = await this.repo.GetDisplayAsync(id);
        Assert.Equal("Some Channel", display!.Artist);
        Assert.Equal(FieldSource.Channel, display.ArtistFrom);
        Assert.Equal(FieldSource.Yt, display.TitleFrom);
    }

    [Fact]
    public async Task User_genres_replace_and_fall_back()
    {
        var id = await this.repo.AddAsync("dQw4w9WgXcQ", "u");
        await this.repo.AddTagAsync(id, "Pop", TagSource.Yt);
        Assert.Equal(FieldSource.Yt, (await this.repo.GetDisplayAsync(id))!.GenresFrom);

        await this.repo.SetUserTagsAsync(id, ["Synth-Pop", "Dance"]);
        var display = await this.repo.GetDisplayAsync(id);
        Assert.Equal(["Dance", "Synth-Pop"], display!.GenreList);
        Assert.Equal(FieldSource.User, display.GenresFrom);

        await this.repo.SetUserTagsAsync(id, ["Dance"]);
        Assert.Equal(["Dance"], (await this.repo.GetDisplayAsync(id))!.GenreList);

        await this.repo.SetUserTagsAsync(id, []);
        display = await this.repo.GetDisplayAsync(id);
        Assert.Equal(["Pop"], display!.GenreList);
        Assert.Equal(FieldSource.Yt, display.GenresFrom);
    }

    [Fact]
    public async Task Tags_are_stale_after_edits_until_written()
    {
        var id = await this.repo.AddAsync("dQw4w9WgXcQ", "u");
        await this.repo.SetUserFieldsAsync(id, new UserFields("Rick", null, null, null, null, null));
        Assert.False((await this.repo.GetDisplayAsync(id))!.TagsStale); // no file yet

        await this.repo.UpdateDownloadStateAsync(id, DownloadStatus.Done, "Rick - Title.opus", "opus");
        Assert.True((await this.repo.GetDisplayAsync(id))!.TagsStale);

        await this.repo.MarkTagsWrittenAsync(id);
        Assert.False((await this.repo.GetDisplayAsync(id))!.TagsStale);

        await this.repo.SetRatingAsync(id, 4);
        Assert.False((await this.repo.GetDisplayAsync(id))!.TagsStale);

        await this.repo.SetUserTagsAsync(id, ["Pop"]);
        Assert.True((await this.repo.GetDisplayAsync(id))!.TagsStale);
    }

    [Fact]
    public async Task Interrupted_download_is_pending_again()
    {
        var interrupted = await this.repo.AddAsync("dQw4w9WgXcQ", "u1");
        var done = await this.repo.AddAsync("9bZkp7q19f0", "u2");
        await this.repo.UpdateDownloadStateAsync(interrupted, DownloadStatus.Downloading);
        await this.repo.UpdateDownloadStateAsync(done, DownloadStatus.Done, "Artist - Title.opus", "opus");

        Assert.Equal(1, await this.repo.ResetInterruptedDownloadsAsync());
        Assert.Equal(DownloadStatus.Pending, (await this.repo.GetDisplayAsync(interrupted))!.DownloadStatus);
        Assert.Equal(DownloadStatus.Done, (await this.repo.GetDisplayAsync(done))!.DownloadStatus);
    }
}
