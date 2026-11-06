using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.Services;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Finding the same song from different videos, and keeping one of them.</summary>
public sealed class DuplicateTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"tapedeck-duplicates-{Guid.NewGuid():N}");
    private AppPaths paths = null!;
    private TrackRepository tracks = null!;
    private PlaylistRepository playlists = null!;
    private LibraryFiles files = null!;
    private PlaylistImportService importer = null!;

    public async Task InitializeAsync()
    {
        this.paths = new AppPaths(Path.Combine(this.root, "data"), Path.Combine(this.root, "library"));
        this.paths.EnsureDirectories();
        var factory = new SqliteConnectionFactory(this.paths.DatabasePath);
        await new Migrator(factory).MigrateAsync();
        this.tracks = new TrackRepository(factory);
        this.playlists = new PlaylistRepository(factory);
        this.files = new LibraryFiles(this.tracks, this.paths);
        // only known videos are imported here, so yt-dlp is never started
        var trackImporter = new TrackImportService(this.tracks, new MetadataService(new ToolService(this.paths)),
            new ThumbnailService(this.paths, new HttpClient()));
        this.importer = new PlaylistImportService(this.tracks, this.playlists, trackImporter);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.root, recursive: true);
        return Task.CompletedTask;
    }

    private static TrackDisplay Track(long id, string artist, string title, string? recording = null) =>
        new() { Id = id, Artist = artist, Title = title, MbRecordingId = recording };

    [Fact]
    public void Same_recording_is_one_song_whatever_the_video_says()
    {
        var groups = DuplicateFinder.Groups([
            Track(5, "Kendrick Lamar", "Alright", "rec-alright"),
            Track(51, "Kendrick Lamar", "Alright", "rec-alright"),
            Track(75, "Kendrick Lamar", "Alright", "rec-alright"),
            Track(4, "Kendrick Lamar", "HUMBLE.", "rec-humble"),
        ]);

        Assert.Equal([[5L, 51L, 75L]], groups);
    }

    [Fact]
    public void Different_recordings_with_the_same_name_stay_apart()
    {
        var groups = DuplicateFinder.Groups([
            Track(1, "Kendrick Lamar", "Swimming Pools (Drank)", "rec-album"),
            Track(2, "Kendrick Lamar", "Swimming Pools (Drank)", "rec-extended"),
        ]);

        Assert.Empty(groups);
    }

    [Fact]
    public void A_track_without_match_joins_the_song_with_its_name()
    {
        var groups = DuplicateFinder.Groups([
            Track(23, "Kendrick Lamar", "King Kunta", "rec-kunta"),
            Track(68, "Kendrick Lamar", "King Kunta", "rec-kunta"),
            // case, accents and punctuation do not matter
            Track(90, "kendrick lamar", "King Kunta!"),
            Track(91, "Wiz Khalifa", "Stayin Out All Night"),
            Track(92, "Wiz Khalifa", "Stayin' Out All Night"),
        ]);

        Assert.Equal([[23L, 68L, 90L], [91L, 92L]], groups);
    }

    [Fact]
    public void A_name_shared_by_two_recordings_takes_no_unmatched_track()
    {
        var groups = DuplicateFinder.Groups([
            Track(1, "Juicy J", "Intro", "rec-1"),
            Track(2, "Juicy J", "Intro", "rec-2"),
            Track(3, "Juicy J", "Intro"),
        ]);

        Assert.Empty(groups);
    }

    private async Task<long> AddAsync(string videoId, string fileName, int? rating = null)
    {
        var id = await this.tracks.AddAsync(videoId, VideoId.WatchUrl(videoId));
        await this.tracks.UpdateDownloadStateAsync(id, DownloadStatus.Done, fileName, "opus");
        await File.WriteAllTextAsync(this.paths.ToLibraryPath(fileName), fileName);
        if (rating is not null)
            await this.tracks.SetRatingAsync(id, rating);
        return id;
    }

    [Fact]
    public async Task Keeping_one_moves_playlist_places_and_rating_and_removes_the_others()
    {
        var video = await this.AddAsync("aaaaaaaaaaa", "Kendrick Lamar - Alright.opus", rating: 5);
        var topic = await this.AddAsync("bbbbbbbbbbb", "Kendrick Lamar - Alright [bbbbbbbbbbb].opus");
        var lyrics = await this.AddAsync("ccccccccccc", "Kendrick Lamar - Alright [ccccccccccc].opus");
        var other = await this.AddAsync("ddddddddddd", "Kendrick Lamar - HUMBLE..opus");
        var both = await this.playlists.CreateAsync("Both");
        await this.playlists.AddTracksAsync(both, [video, other, topic]);
        var onlyCopies = await this.playlists.CreateAsync("Only copies");
        await this.playlists.AddTracksAsync(onlyCopies, [other, video, lyrics]);

        await this.files.KeepAsync(topic, [video, topic, lyrics], deleteFiles: true);

        // the kept track is in each playlist once; a copy takes the place where the kept track was missing
        Assert.Equal([other, topic], (await this.playlists.GetTracksAsync(both)).Select(t => t.Id));
        Assert.Equal([other, topic], (await this.playlists.GetTracksAsync(onlyCopies)).Select(t => t.Id));
        Assert.Equal(5, (await this.tracks.GetAsync(topic))!.Rating);
        Assert.Null(await this.tracks.GetAsync(video));
        Assert.Null(await this.tracks.GetAsync(lyrics));
        Assert.False(File.Exists(this.paths.ToLibraryPath("Kendrick Lamar - Alright.opus")));
        Assert.True(File.Exists(this.paths.ToLibraryPath("Kendrick Lamar - Alright [bbbbbbbbbbb].opus")));
        Assert.Equal(topic, await this.tracks.GetAliasTargetAsync("aaaaaaaaaaa"));
    }

    [Fact]
    public async Task A_sync_does_not_bring_a_removed_copy_back()
    {
        var video = await this.AddAsync("aaaaaaaaaaa", "A.opus");
        var topic = await this.AddAsync("bbbbbbbbbbb", "B.opus");
        var list = await this.playlists.CreateAsync("Mix", PlaylistLink.Url("PLabc"));
        await this.playlists.AddTracksAsync(list, [video, topic]);
        await this.files.KeepAsync(topic, [video, topic], deleteFiles: false);

        var result = await this.importer.ImportAsync(list, new YtPlaylist("PLabc", "Mix", null,
            [new("aaaaaaaaaaa", "A"), new("bbbbbbbbbbb", "B")]));

        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.NewTracks);
        Assert.Equal([topic], (await this.playlists.GetTracksAsync(list)).Select(t => t.Id));
        Assert.True(File.Exists(this.paths.ToLibraryPath("A.opus")));
    }

    [Fact]
    public async Task Merging_twice_points_older_aliases_to_the_new_track()
    {
        var a = await this.AddAsync("aaaaaaaaaaa", "A.opus");
        var b = await this.AddAsync("bbbbbbbbbbb", "B.opus");
        var c = await this.AddAsync("ccccccccccc", "C.opus");
        await this.files.KeepAsync(b, [a, b], deleteFiles: true);

        await this.files.KeepAsync(c, [b, c], deleteFiles: true);

        var aliases = await this.tracks.ListAliasesAsync();
        Assert.Equal(c, aliases["aaaaaaaaaaa"]);
        Assert.Equal(c, aliases["bbbbbbbbbbb"]);
    }
}
