using Microsoft.Data.Sqlite;
using Tapedeck.Core.Models;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Checks the best-value selection in <c>tracks_display</c> on a freshly migrated database.</summary>
public sealed class TracksDisplayTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tapedeck-test-{Guid.NewGuid():N}.db");
    private TrackRepository _repo = null!;

    public async Task InitializeAsync()
    {
        var factory = new SqliteConnectionFactory(_dbPath);
        Assert.Equal(Migrator.LatestVersion, await new Migrator(factory).MigrateAsync());
        _repo = new TrackRepository(factory);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            File.Delete(file);
        return Task.CompletedTask;
    }

    private static YtMetadata Meta(string? artist = null, string? track = null) => new(
        "dQw4w9WgXcQ", "Rick Astley - Never Gonna Give You Up (Official Video)", "RickAstleyVEVO",
        "20091025", 213, track, artist, null, null, "Rick Astley", "Never Gonna Give You Up", [], "{}");

    [Fact]
    public async Task Duplicate_video_id_returns_same_track()
    {
        var first = await _repo.AddAsync("dQw4w9WgXcQ", "https://youtu.be/dQw4w9WgXcQ");
        var second = await _repo.AddAsync("dQw4w9WgXcQ", "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Prefers_user_over_yt_over_parsed()
    {
        var id = await _repo.AddAsync("dQw4w9WgXcQ", "u");

        await _repo.UpdateYtMetadataAsync(id, Meta());
        var display = await _repo.GetDisplayAsync(id);
        Assert.Equal("Rick Astley", display!.Artist);
        Assert.Equal("Never Gonna Give You Up", display.Title);
        Assert.Equal("Rick Astley", display.AlbumArtist);

        await _repo.UpdateYtMetadataAsync(id, Meta(artist: "Rick Astley (yt)", track: "NGGYU (yt)"));
        display = await _repo.GetDisplayAsync(id);
        Assert.Equal("Rick Astley (yt)", display!.Artist);
        Assert.Equal("NGGYU (yt)", display.Title);

        await _repo.SetUserFieldsAsync(id, new UserFields("Rick", null, "Whenever You Need Somebody", "Various", 1987, null));
        display = await _repo.GetDisplayAsync(id);
        Assert.Equal("Rick", display!.Artist);
        Assert.Equal("NGGYU (yt)", display.Title);
        Assert.Equal("Whenever You Need Somebody", display.Album);
        Assert.Equal("Various", display.AlbumArtist);
        Assert.Equal(1987, display.Year);
    }

    [Fact]
    public async Task Genres_come_from_best_source_only()
    {
        var id = await _repo.AddAsync("dQw4w9WgXcQ", "u");
        await _repo.AddTagAsync(id, "Entertainment", TagSource.Yt);
        Assert.Equal(["Entertainment"], (await _repo.GetDisplayAsync(id))!.GenreList);

        await _repo.AddTagAsync(id, "Pop", TagSource.Lastfm);
        await _repo.AddTagAsync(id, "Dance", TagSource.Lastfm);
        Assert.Equal(["Dance", "Pop"], (await _repo.GetDisplayAsync(id))!.GenreList);

        await _repo.AddTagAsync(id, "Synth-Pop", TagSource.User);
        Assert.Equal(["Synth-Pop"], (await _repo.GetDisplayAsync(id))!.GenreList);
    }

    [Fact]
    public async Task Cover_falls_back_to_thumbnail()
    {
        var id = await _repo.AddAsync("dQw4w9WgXcQ", "u");
        await _repo.SetThumbPathAsync(id, "thumb.jpg");
        Assert.Equal("thumb.jpg", (await _repo.GetDisplayAsync(id))!.Cover);

        await _repo.SetCoverAsync(id, "cover.jpg", "caa");
        Assert.Equal("cover.jpg", (await _repo.GetDisplayAsync(id))!.Cover);
    }

    [Fact]
    public async Task Sources_follow_the_best_value()
    {
        var id = await _repo.AddAsync("dQw4w9WgXcQ", "u");
        var display = await _repo.GetDisplayAsync(id);
        Assert.Equal(FieldSource.None, display!.ArtistFrom);
        Assert.Equal(FieldSource.None, display.TitleFrom);

        await _repo.UpdateYtMetadataAsync(id, Meta());
        display = await _repo.GetDisplayAsync(id);
        Assert.Equal(FieldSource.Parsed, display!.ArtistFrom);
        Assert.Equal(FieldSource.Parsed, display.TitleFrom);
        Assert.Equal(FieldSource.Derived, display.AlbumArtistFrom);
        Assert.Equal(FieldSource.None, display.AlbumFrom);
        Assert.Equal(FieldSource.None, display.YearFrom);
        Assert.Equal("RickAstleyVEVO", display.Channel);
        Assert.Equal("20091025", display.UploadDate);

        await _repo.UpdateYtMetadataAsync(id, Meta(artist: "Rick Astley", track: "NGGYU"));
        await _repo.SetUserFieldsAsync(id, new UserFields(null, null, "Whenever You Need Somebody", "Various", 1987, null));
        display = await _repo.GetDisplayAsync(id);
        Assert.Equal(FieldSource.Yt, display!.ArtistFrom);
        Assert.Equal(FieldSource.Yt, display.TitleFrom);
        Assert.Equal(FieldSource.User, display.AlbumFrom);
        Assert.Equal(FieldSource.User, display.AlbumArtistFrom);
        Assert.Equal(FieldSource.User, display.YearFrom);
    }

    [Fact]
    public async Task Artist_falls_back_to_channel()
    {
        var id = await _repo.AddAsync("dQw4w9WgXcQ", "u");
        await _repo.UpdateYtMetadataAsync(id, new YtMetadata(
            "dQw4w9WgXcQ", "Untitled upload", "Some Channel", "20240101", 100, null, null, null, null, null, null, [], "{}"));

        var display = await _repo.GetDisplayAsync(id);
        Assert.Equal("Some Channel", display!.Artist);
        Assert.Equal(FieldSource.Channel, display.ArtistFrom);
        Assert.Equal(FieldSource.Yt, display.TitleFrom);
    }

    [Fact]
    public async Task User_genres_replace_and_fall_back()
    {
        var id = await _repo.AddAsync("dQw4w9WgXcQ", "u");
        await _repo.AddTagAsync(id, "Pop", TagSource.Yt);
        Assert.Equal(FieldSource.Yt, (await _repo.GetDisplayAsync(id))!.GenresFrom);

        await _repo.SetUserTagsAsync(id, ["Synth-Pop", "Dance"]);
        var display = await _repo.GetDisplayAsync(id);
        Assert.Equal(["Dance", "Synth-Pop"], display!.GenreList);
        Assert.Equal(FieldSource.User, display.GenresFrom);

        await _repo.SetUserTagsAsync(id, ["Dance"]);
        Assert.Equal(["Dance"], (await _repo.GetDisplayAsync(id))!.GenreList);

        await _repo.SetUserTagsAsync(id, []);
        display = await _repo.GetDisplayAsync(id);
        Assert.Equal(["Pop"], display!.GenreList);
        Assert.Equal(FieldSource.Yt, display.GenresFrom);
    }

    [Fact]
    public async Task Tags_are_stale_after_edits_until_written()
    {
        var id = await _repo.AddAsync("dQw4w9WgXcQ", "u");
        await _repo.SetUserFieldsAsync(id, new UserFields("Rick", null, null, null, null, null));
        Assert.False((await _repo.GetDisplayAsync(id))!.TagsStale); // no file yet

        await _repo.UpdateDownloadStateAsync(id, DownloadStatus.Done, "Rick - Title.opus", "opus");
        Assert.True((await _repo.GetDisplayAsync(id))!.TagsStale);

        await _repo.MarkTagsWrittenAsync(id);
        Assert.False((await _repo.GetDisplayAsync(id))!.TagsStale);

        await _repo.SetRatingAsync(id, 4);
        Assert.False((await _repo.GetDisplayAsync(id))!.TagsStale);

        await _repo.SetUserTagsAsync(id, ["Pop"]);
        Assert.True((await _repo.GetDisplayAsync(id))!.TagsStale);
    }

    [Fact]
    public async Task Interrupted_download_is_pending_again()
    {
        var interrupted = await _repo.AddAsync("dQw4w9WgXcQ", "u1");
        var done = await _repo.AddAsync("9bZkp7q19f0", "u2");
        await _repo.UpdateDownloadStateAsync(interrupted, DownloadStatus.Downloading);
        await _repo.UpdateDownloadStateAsync(done, DownloadStatus.Done, "Artist - Title.opus", "opus");

        Assert.Equal(1, await _repo.ResetInterruptedDownloadsAsync());
        Assert.Equal(DownloadStatus.Pending, (await _repo.GetDisplayAsync(interrupted))!.DownloadStatus);
        Assert.Equal(DownloadStatus.Done, (await _repo.GetDisplayAsync(done))!.DownloadStatus);
    }
}
