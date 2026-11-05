using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.Services;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Taking over files that are already in the library folder instead of downloading them again.</summary>
public sealed class ExistingFileFinderTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"tapedeck-finder-{Guid.NewGuid():N}");
    private AppPaths paths = null!;
    private TrackRepository tracks = null!;
    private ExistingFileFinder finder = null!;

    public async Task InitializeAsync()
    {
        this.paths = new AppPaths(Path.Combine(this.root, "data"), Path.Combine(this.root, "library"));
        this.paths.EnsureDirectories();
        var factory = new SqliteConnectionFactory(this.paths.DatabasePath);
        await new Migrator(factory).MigrateAsync();
        this.tracks = new TrackRepository(factory);
        this.finder = new ExistingFileFinder(this.tracks, this.paths);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.root, recursive: true);
        return Task.CompletedTask;
    }

    private static TrackDisplay Track(long id, string videoId, string artist, string title) =>
        new() { Id = id, VideoId = videoId, Artist = artist, Title = title };

    private async Task<long> TrackAsync(string videoId, string artist, string title)
    {
        var id = await this.tracks.AddAsync(videoId, VideoId.WatchUrl(videoId));
        await this.tracks.UpdateYtMetadataAsync(id, new YtMetadata(videoId, $"{artist} - {title}", "Channel",
            "20200101", 200, title, artist, null, null, artist, title, [], "{}"));
        return id;
    }

    [Theory]
    [InlineData("A - One [aaaaaaaaaaa].opus", "aaaaaaaaaaa")]
    [InlineData("A - One.opus", null)]
    [InlineData("A - One [too-short].opus", null)]
    public void Reads_the_video_id_from_the_name(string fileName, string? expected) =>
        Assert.Equal(expected, ExistingFileFinder.VideoIdInName(fileName));

    [Fact]
    public void Video_id_wins_and_names_only_match_files_without_one()
    {
        var files = new List<LibraryFile>
        {
            new("Renamed by hand.opus", "aaaaaaaaaaa"),
            // named like track b, but its tag says it is another video
            new("B - Two.opus", "zzzzzzzzzzz"),
            new("C - Three.opus", null),
        };

        var matches = ExistingFileFinder.Match(
            [Track(1, "aaaaaaaaaaa", "A", "One"), Track(2, "bbbbbbbbbbb", "B", "Two"), Track(3, "ccccccccccc", "c", "three")],
            files);

        Assert.Equal("Renamed by hand.opus", matches[1]);
        Assert.False(matches.ContainsKey(2));
        Assert.Equal("C - Three.opus", matches[3]);
    }

    [Fact]
    public void A_file_goes_to_one_track_only()
    {
        var matches = ExistingFileFinder.Match(
            [Track(1, "aaaaaaaaaaa", "Same", "Song"), Track(2, "bbbbbbbbbbb", "Same", "Song")],
            [new LibraryFile("Same - Song.opus", null)]);

        Assert.Equal(["Same - Song.opus"], matches.Values);
    }

    [Fact]
    public async Task Takes_over_files_and_leaves_claimed_ones_alone()
    {
        var a = await this.TrackAsync("aaaaaaaaaaa", "A", "One");
        var b = await this.TrackAsync("bbbbbbbbbbb", "B", "Two");
        var owner = await this.TrackAsync("ooooooooooo", "B", "Two (live)");
        // not real audio: tags cannot be read, so the names decide
        await File.WriteAllTextAsync(this.paths.ToLibraryPath("A - One.opus"), "audio");
        await File.WriteAllTextAsync(this.paths.ToLibraryPath("B - Two [bbbbbbbbbbb].opus"), "audio");
        await File.WriteAllTextAsync(this.paths.ToLibraryPath("Other.opus"), "audio");
        await this.tracks.UpdateDownloadStateAsync(owner, DownloadStatus.Done, "Other.opus", "opus");

        var adopted = await this.finder.AdoptAsync();

        Assert.Equal([a, b], adopted.Order());
        var trackA = await this.tracks.GetDisplayAsync(a);
        Assert.Equal(DownloadStatus.Done, trackA!.DownloadStatus);
        Assert.Equal("A - One.opus", trackA.FilePath);
        Assert.Equal("B - Two [bbbbbbbbbbb].opus", (await this.tracks.GetDisplayAsync(b))!.FilePath);
        Assert.Empty(await this.finder.AdoptAsync());
    }

    [Fact]
    public async Task Only_looks_at_the_given_tracks()
    {
        var a = await this.TrackAsync("aaaaaaaaaaa", "A", "One");
        var b = await this.TrackAsync("bbbbbbbbbbb", "B", "Two");
        await File.WriteAllTextAsync(this.paths.ToLibraryPath("A - One.opus"), "audio");
        await File.WriteAllTextAsync(this.paths.ToLibraryPath("B - Two.opus"), "audio");

        Assert.Equal([b], await this.finder.AdoptAsync([b]));
        Assert.Null((await this.tracks.GetDisplayAsync(a))!.FilePath);
    }
}
