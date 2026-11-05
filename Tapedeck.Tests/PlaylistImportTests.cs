using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Csv;
using Tapedeck.Core.Models;
using Tapedeck.Core.Services;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>YouTube playlist links, the flat playlist JSON and the import into a playlist, without network.</summary>
public sealed class PlaylistImportTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"tapedeck-import-{Guid.NewGuid():N}");
    private TrackRepository tracks = null!;
    private PlaylistRepository playlists = null!;
    private PlaylistImportService importer = null!;
    private CsvImportService csvImporter = null!;

    public async Task InitializeAsync()
    {
        var paths = new AppPaths(Path.Combine(this.root, "data"), Path.Combine(this.root, "library"));
        paths.EnsureDirectories();
        var factory = new SqliteConnectionFactory(paths.DatabasePath);
        await new Migrator(factory).MigrateAsync();
        this.tracks = new TrackRepository(factory);
        this.playlists = new PlaylistRepository(factory);
        // only known videos are imported here, so yt-dlp is never started
        var tools = new ToolService(paths);
        var trackImporter = new TrackImportService(this.tracks, new MetadataService(tools), new ThumbnailService(paths, new HttpClient()));
        this.importer = new PlaylistImportService(this.tracks, this.playlists, trackImporter);
        this.csvImporter = new CsvImportService(this.tracks, trackImporter);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.root, recursive: true);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PLabc123_-x", "PLabc123_-x")]
    [InlineData("youtube.com/playlist?list=PLabc123", "PLabc123")]
    [InlineData("https://music.youtube.com/playlist?list=OLAK5uy_abc", "OLAK5uy_abc")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PLabc123&index=2", "PLabc123")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?list=PLabc123", "PLabc123")]
    [InlineData("https://music.youtube.com/playlist?list=RDCLAK5uy_abc", "RDCLAK5uy_abc")]
    public void Recognizes_playlist_links(string link, string expected)
    {
        Assert.True(PlaylistLink.TryParse(link, out var listId));
        Assert.Equal(expected, listId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://evil.com/playlist?list=PLabc123")]
    [InlineData("https://www.youtube.com/playlist?list=")]
    public void Rejects_links_without_playlist(string link) => Assert.False(PlaylistLink.TryParse(link, out _));

    [Fact]
    public void Mixes_are_no_playlists_but_keep_their_video()
    {
        const string LINK = "https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=RDdQw4w9WgXcQ&start_radio=1";
        Assert.False(PlaylistLink.TryParse(LINK, out _));
        Assert.True(PlaylistLink.IsMixLink(LINK));
        Assert.True(VideoId.TryParse(LINK, out var videoId));
        Assert.Equal("dQw4w9WgXcQ", videoId);
    }

    [Fact]
    public void Flat_playlist_skips_unavailable_and_repeated_videos()
    {
        const string JSON = """
            {
              "id": "PLabc", "title": "Road Trip", "channel": "Someone", "_type": "playlist",
              "entries": [
                { "id": "aaaaaaaaaaa", "title": "A - One", "duration": 200 },
                { "id": "bbbbbbbbbbb", "title": "[Private video]", "duration": null },
                { "id": "ccccccccccc", "title": "[Deleted video]" },
                { "id": "ddddddddddd", "title": "D - Four" },
                { "id": "aaaaaaaaaaa", "title": "A - One" },
                { "id": "not-an-id" }
              ]
            }
            """;

        var playlist = MetadataService.ParsePlaylist(JSON);

        Assert.Equal("PLabc", playlist.Id);
        Assert.Equal("Road Trip", playlist.Title);
        Assert.Equal(["aaaaaaaaaaa", "ddddddddddd"], playlist.Entries.Select(e => e.VideoId));
    }

    [Fact]
    public async Task Linked_playlists_are_found_by_their_source()
    {
        var url = PlaylistLink.Url("PLabc");
        var id = await this.playlists.CreateAsync("Road Trip", url);

        Assert.Equal(id, (await this.playlists.FindBySourceAsync(url))?.Id);
        Assert.Equal(url, (await this.playlists.GetAsync(id))?.SourceUrl);
        Assert.Null(await this.playlists.FindBySourceAsync(PlaylistLink.Url("PLother")));
        await Assert.ThrowsAsync<SqliteException>(() => this.playlists.CreateAsync("Copy", url));
    }

    [Fact]
    public async Task Sync_appends_new_videos_in_youtube_order_and_keeps_the_rest()
    {
        var a = await this.tracks.AddAsync("aaaaaaaaaaa", VideoId.WatchUrl("aaaaaaaaaaa"));
        var b = await this.tracks.AddAsync("bbbbbbbbbbb", VideoId.WatchUrl("bbbbbbbbbbb"));
        var c = await this.tracks.AddAsync("ccccccccccc", VideoId.WatchUrl("ccccccccccc"));
        var own = await this.tracks.AddAsync("ooooooooooo", VideoId.WatchUrl("ooooooooooo"));
        var list = await this.playlists.CreateAsync("Mix", PlaylistLink.Url("PLabc"));
        await this.playlists.AddTracksAsync(list, [own, b]);

        var source = new YtPlaylist("PLabc", "Mix", null,
            [new("ccccccccccc", "C"), new("bbbbbbbbbbb", "B"), new("aaaaaaaaaaa", "A")]);
        var result = await this.importer.ImportAsync(list, source);

        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.NewTracks);
        Assert.Empty(result.Errors);
        Assert.Equal([own, b, c, a], (await this.playlists.GetTracksAsync(list)).Select(t => t.Id));

        var again = await this.importer.ImportAsync(list, source);
        Assert.Equal(0, again.Added);
    }

    [Fact]
    public async Task Csv_import_keeps_the_file_order_and_applies_corrections()
    {
        var a = await this.tracks.AddAsync("aaaaaaaaaaa", VideoId.WatchUrl("aaaaaaaaaaa"));
        var b = await this.tracks.AddAsync("bbbbbbbbbbb", VideoId.WatchUrl("bbbbbbbbbbb"));
        CsvTrack Row(int line, string videoId, string? artist = null, int? rating = null) =>
            new(line, videoId, VideoId.WatchUrl(videoId), artist, null, null, null, null, [], rating);

        var result = await this.csvImporter.ImportAsync([Row(2, "bbbbbbbbbbb", "Fixed Artist", 4), Row(3, "aaaaaaaaaaa")]);

        Assert.Equal([b, a], result.TrackIds);
        Assert.Empty(result.Errors);
        var trackB = await this.tracks.GetDisplayAsync(b);
        Assert.Equal("Fixed Artist", trackB!.Artist);
        Assert.Equal(4, trackB.Rating);
    }
}
