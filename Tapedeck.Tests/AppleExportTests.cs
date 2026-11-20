using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Apple;
using Tapedeck.Core.Models;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>The Apple export: copies, tags, renames and leftovers, with a fake encoder instead of ffmpeg.</summary>
public sealed class AppleExportTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"tapedeck-test-{Guid.NewGuid():N}");
    private AppPaths paths = null!;
    private TrackRepository tracks = null!;
    private PlaylistRepository playlists = null!;
    private AppleExportRepository exports = null!;
    private FakeEncoder encoder = null!;
    private AppleExporter exporter = null!;

    public async Task InitializeAsync()
    {
        this.paths = new AppPaths(Path.Combine(this.root, "data"), Path.Combine(this.root, "library"), Path.Combine(this.root, "apple"));
        this.paths.EnsureDirectories();
        var factory = new SqliteConnectionFactory(this.paths.DatabasePath);
        await new Migrator(factory).MigrateAsync();
        this.tracks = new TrackRepository(factory);
        this.playlists = new PlaylistRepository(factory);
        this.exports = new AppleExportRepository(factory);
        this.encoder = new FakeEncoder();
        this.exporter = new AppleExporter(this.tracks, this.exports, this.playlists, this.encoder, this.paths);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.root, recursive: true);
        return Task.CompletedTask;
    }

    private async Task<long> AddWithFileAsync(string videoId, string fileName)
    {
        File.Copy(Fixture("tone-2s.opus"), this.paths.ToLibraryPath(fileName));
        var id = await this.tracks.AddAsync(videoId, $"https://www.youtube.com/watch?v={videoId}");
        await this.tracks.UpdateYtMetadataAsync(id, new YtMetadata(
            videoId, "Daft Punk - Digital Love", "Channel", "20200101", 2, null, null, null, null,
            "Daft Punk", "Digital Love", [], "{}"));
        await this.tracks.UpdateDownloadStateAsync(id, DownloadStatus.Done, fileName, "opus");
        return id;
    }

    private async Task ExportAllAsync()
    {
        foreach (var candidate in await this.exports.ListPendingAsync())
            await this.exporter.ExportAsync(candidate);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Audio", name);

    [Fact]
    public async Task A_downloaded_track_gets_a_tagged_copy()
    {
        var id = await this.AddWithFileAsync("aaaaaaaaaaa", "Daft Punk - Digital Love.opus");
        await this.tracks.SetUserFieldsAsync(id, new UserFields("Daft Punk", "Digital Love", "Discovery", null, 2001, null));

        await this.ExportAllAsync();

        var copy = Path.Combine(this.paths.AppleExportDir, "Daft Punk - Digital Love.m4a");
        using (var file = TagLib.File.Create(copy))
        {
            Assert.Equal("Digital Love", file.Tag.Title);
            Assert.Equal(["Daft Punk"], file.Tag.Performers);
            Assert.Equal("Discovery", file.Tag.Album);
            Assert.Equal(2001u, file.Tag.Year);
            Assert.Equal("https://www.youtube.com/watch?v=aaaaaaaaaaa", file.Tag.Comment);
        }
        Assert.Equal(new AppleExportCounts(1, 1), await this.exports.CountAsync());
        Assert.Empty(await this.exports.ListPendingAsync());
    }

    [Fact]
    public async Task New_values_only_rewrite_the_tags()
    {
        var id = await this.AddWithFileAsync("aaaaaaaaaaa", "Daft Punk - Digital Love.opus");
        await this.ExportAllAsync();
        await this.tracks.SetUserFieldsAsync(id, new UserFields(null, null, "Discovery", null, null, null));

        var pending = Assert.Single(await this.exports.ListPendingAsync());
        var outcome = await this.exporter.ExportAsync(pending);

        Assert.Equal(AppleExportOutcome.Retagged, outcome);
        Assert.Equal(1, this.encoder.Calls);
        using var file = TagLib.File.Create(Path.Combine(this.paths.AppleExportDir, "Daft Punk - Digital Love.m4a"));
        Assert.Equal("Discovery", file.Tag.Album);
    }

    [Fact]
    public async Task A_missing_copy_is_encoded_again()
    {
        await this.AddWithFileAsync("aaaaaaaaaaa", "Daft Punk - Digital Love.opus");
        await this.ExportAllAsync();
        var copy = Path.Combine(this.paths.AppleExportDir, "Daft Punk - Digital Love.m4a");
        File.Delete(copy);
        var record = Assert.Single((await this.exports.ListAsync()).Values);

        var outcome = await this.exporter.ExportAsync(new AppleExportCandidate(
            record.TrackId, record.SourcePath, record.SourceDownloadedAt, record.SourceEditedAt, record));

        Assert.Equal(AppleExportOutcome.Encoded, outcome);
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public async Task The_copy_follows_a_renamed_library_file()
    {
        var id = await this.AddWithFileAsync("aaaaaaaaaaa", "Daft Punk - Digital Love.opus");
        await this.ExportAllAsync();
        File.Move(this.paths.ToLibraryPath("Daft Punk - Digital Love.opus"), this.paths.ToLibraryPath("Daft Punk - Digital Love (Live).opus"));
        await this.tracks.SetFilePathAsync(id, "Daft Punk - Digital Love (Live).opus");

        await this.ExportAllAsync();

        Assert.Equal(1, this.encoder.Calls);
        Assert.False(File.Exists(Path.Combine(this.paths.AppleExportDir, "Daft Punk - Digital Love.m4a")));
        Assert.True(File.Exists(Path.Combine(this.paths.AppleExportDir, "Daft Punk - Digital Love (Live).m4a")));
    }

    [Fact]
    public async Task Someone_elses_file_keeps_its_name()
    {
        Directory.CreateDirectory(this.paths.AppleExportDir);
        var foreign = Path.Combine(this.paths.AppleExportDir, "Daft Punk - Digital Love.m4a");
        await File.WriteAllTextAsync(foreign, "not ours");
        await this.AddWithFileAsync("aaaaaaaaaaa", "Daft Punk - Digital Love.opus");

        await this.ExportAllAsync();

        Assert.Equal("not ours", await File.ReadAllTextAsync(foreign));
        Assert.True(File.Exists(Path.Combine(this.paths.AppleExportDir, "Daft Punk - Digital Love [aaaaaaaaaaa].m4a")));
    }

    [Fact]
    public async Task The_copy_of_a_removed_track_is_deleted()
    {
        var id = await this.AddWithFileAsync("aaaaaaaaaaa", "Daft Punk - Digital Love.opus");
        await this.ExportAllAsync();
        await this.tracks.DeleteAsync(id);

        Assert.Equal(1, await this.exporter.PruneAsync());

        Assert.False(File.Exists(Path.Combine(this.paths.AppleExportDir, "Daft Punk - Digital Love.m4a")));
        Assert.Empty(await this.exports.ListAsync());
    }

    [Fact]
    public async Task Timed_lyrics_go_into_the_copy_without_times()
    {
        var id = await this.AddWithFileAsync("aaaaaaaaaaa", "Daft Punk - Digital Love.opus");
        await this.tracks.SetUserLyricsAsync(id, "[00:01.00]First line\n[00:02.50]Second line");

        await this.ExportAllAsync();

        using var file = TagLib.File.Create(Path.Combine(this.paths.AppleExportDir, "Daft Punk - Digital Love.m4a"));
        Assert.Equal("First line\nSecond line", file.Tag.Lyrics);
    }

    [Fact]
    public async Task Playlists_point_to_the_copies()
    {
        var id = await this.AddWithFileAsync("aaaaaaaaaaa", "Daft Punk - Digital Love.opus");
        var playlist = await this.playlists.CreateAsync("Road Trip");
        await this.playlists.AddTracksAsync(playlist, [id]);
        await this.ExportAllAsync();

        await this.exporter.SyncPlaylistsAsync();

        var content = await File.ReadAllTextAsync(Path.Combine(this.paths.AppleExportDir, "Road Trip.m3u8"));
        Assert.Contains("\nDaft Punk - Digital Love.m4a\n", content);
        Assert.DoesNotContain(".opus", content);
    }

    [Fact]
    public async Task Removing_everything_leaves_foreign_files()
    {
        await this.AddWithFileAsync("aaaaaaaaaaa", "Daft Punk - Digital Love.opus");
        await this.ExportAllAsync();
        var foreign = Path.Combine(this.paths.AppleExportDir, "mine.m4a");
        await File.WriteAllTextAsync(foreign, "not ours");

        await this.exporter.RemoveAllAsync(this.paths.AppleExportDir);

        Assert.True(File.Exists(foreign));
        Assert.False(File.Exists(Path.Combine(this.paths.AppleExportDir, "Daft Punk - Digital Love.m4a")));
        Assert.Equal(new AppleExportCounts(0, 1), await this.exports.CountAsync());
    }

    [Fact]
    public void Finds_apples_encoder_in_the_list()
    {
        const string LIST = """
             A....D aac                  AAC (Advanced Audio Coding)
             A....D aac_at               aac (AudioToolbox) (codec aac)
            """;
        Assert.True(AacEncoder.HasEncoder(LIST, AacEncoder.APPLE_ENCODER));
        Assert.False(AacEncoder.HasEncoder(LIST.Replace("aac_at", "aac_mf"), AacEncoder.APPLE_ENCODER));
    }

    /// <summary>Copies the AAC fixture instead of running ffmpeg.</summary>
    private sealed class FakeEncoder : IAacEncoder
    {
        public int Calls { get; private set; }

        public bool IsAvailable => true;

        public Task EncodeAsync(string source, string target, CancellationToken cancellation = default)
        {
            this.Calls++;
            File.Copy(Fixture("tone-2s.m4a"), target, overwrite: true);
            return Task.CompletedTask;
        }
    }
}
