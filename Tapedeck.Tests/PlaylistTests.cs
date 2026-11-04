using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.Playlists;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Playlists in the database and their .m3u8 files, with a real database and temp folders.</summary>
public sealed class PlaylistTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"tapedeck-playlists-{Guid.NewGuid():N}");
    private AppPaths paths = null!;
    private TrackRepository tracks = null!;
    private PlaylistRepository playlists = null!;
    private PlaylistExporter exporter = null!;

    public async Task InitializeAsync()
    {
        this.paths = new AppPaths(Path.Combine(this.root, "data"), Path.Combine(this.root, "library"));
        this.paths.EnsureDirectories();
        var factory = new SqliteConnectionFactory(this.paths.DatabasePath);
        await new Migrator(factory).MigrateAsync();
        this.tracks = new TrackRepository(factory);
        this.playlists = new PlaylistRepository(factory);
        this.exporter = new PlaylistExporter(this.playlists, this.paths);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.root, recursive: true);
        return Task.CompletedTask;
    }

    private async Task<long> TrackAsync(string videoId, string artist, string title, bool downloaded = true)
    {
        var id = await this.tracks.AddAsync(videoId, videoId);
        await this.tracks.UpdateYtMetadataAsync(id, new YtMetadata(videoId, $"{artist} - {title}", "Channel",
            "20200101", 200, title, artist, null, null, artist, title, [], "{}"));
        if (downloaded)
        {
            var file = $"{artist} - {title}.opus";
            await this.tracks.UpdateDownloadStateAsync(id, DownloadStatus.Done, file, "opus");
            await File.WriteAllTextAsync(this.paths.ToLibraryPath(file), "audio");
        }
        return id;
    }

    private async Task<IReadOnlyList<string>> OrderAsync(long playlistId) =>
        (await this.playlists.GetTracksAsync(playlistId)).Select(t => t.Title).ToList();

    [Fact]
    public async Task Names_are_unique_regardless_of_case()
    {
        var id = await this.playlists.CreateAsync("Summer");
        Assert.True(await this.playlists.NameExistsAsync("SUMMER"));
        Assert.False(await this.playlists.NameExistsAsync("summer", exceptId: id));
        await Assert.ThrowsAsync<SqliteException>(() => this.playlists.CreateAsync("summer"));
    }

    [Fact]
    public async Task Adding_appends_skips_duplicates_and_counts()
    {
        var a = await this.TrackAsync("aaaaaaaaaaa", "A", "One");
        var b = await this.TrackAsync("bbbbbbbbbbb", "B", "Two", downloaded: false);
        var list = await this.playlists.CreateAsync("Mix");

        Assert.Equal(2, await this.playlists.AddTracksAsync(list, [a, b]));
        Assert.Equal(0, await this.playlists.AddTracksAsync(list, [a]));
        Assert.Equal(["One", "Two"], await this.OrderAsync(list));

        Assert.Equal(2, (await this.playlists.GetAsync(list))!.TrackCount);
    }

    [Fact]
    public async Task Moving_and_removing_keep_the_order_without_gaps()
    {
        var a = await this.TrackAsync("aaaaaaaaaaa", "A", "One");
        var b = await this.TrackAsync("bbbbbbbbbbb", "B", "Two");
        var c = await this.TrackAsync("ccccccccccc", "C", "Three");
        var list = await this.playlists.CreateAsync("Mix");
        await this.playlists.AddTracksAsync(list, [a, b, c]);

        await this.playlists.MoveTrackAsync(list, c, 0);
        Assert.Equal(["Three", "One", "Two"], await this.OrderAsync(list));

        await this.playlists.RemoveTrackAsync(list, a);
        await this.playlists.AddTracksAsync(list, [a]);
        Assert.Equal(["Three", "Two", "One"], await this.OrderAsync(list));
    }

    [Fact]
    public async Task Deleting_a_track_removes_it_from_playlists()
    {
        var a = await this.TrackAsync("aaaaaaaaaaa", "A", "One");
        var list = await this.playlists.CreateAsync("Mix");
        await this.playlists.AddTracksAsync(list, [a]);

        await this.tracks.DeleteAsync(a);
        Assert.Empty(await this.OrderAsync(list));
    }

    [Fact]
    public async Task Export_lists_downloaded_files_in_order()
    {
        var a = await this.TrackAsync("aaaaaaaaaaa", "A", "One");
        var b = await this.TrackAsync("bbbbbbbbbbb", "B", "Two", downloaded: false);
        var c = await this.TrackAsync("ccccccccccc", "C", "Three");
        var list = await this.playlists.CreateAsync("Road Trip");
        await this.playlists.AddTracksAsync(list, [c, b, a]);

        await this.exporter.SyncAsync();

        var content = await File.ReadAllTextAsync(this.paths.ToLibraryPath("Road Trip.m3u8"));
        Assert.Equal(
            "#EXTM3U\n#PLAYLIST:Road Trip\n#TAPEDECK\n" +
            "#EXTINF:200,C - Three\nC - Three.opus\n" +
            "#EXTINF:200,A - One\nA - One.opus\n",
            content);
    }

    [Fact]
    public async Task Renamed_or_deleted_playlists_leave_no_files_but_foreign_ones_stay()
    {
        var list = await this.playlists.CreateAsync("Old: Name?");
        var foreign = this.paths.ToLibraryPath("Mine.m3u8");
        await File.WriteAllTextAsync(foreign, "#EXTM3U\nsomething.opus\n");
        await this.exporter.SyncAsync();
        Assert.True(File.Exists(this.paths.ToLibraryPath("Old - Name.m3u8")));

        await this.playlists.RenameAsync(list, "New");
        await this.exporter.SyncAsync();
        Assert.False(File.Exists(this.paths.ToLibraryPath("Old - Name.m3u8")));
        Assert.True(File.Exists(this.paths.ToLibraryPath("New.m3u8")));

        await this.playlists.DeleteAsync(list);
        await this.exporter.SyncAsync();
        Assert.False(File.Exists(this.paths.ToLibraryPath("New.m3u8")));
        Assert.True(File.Exists(foreign));
    }
}
