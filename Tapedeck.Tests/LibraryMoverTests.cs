using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Lyrics;
using Tapedeck.Core.Models;
using Tapedeck.Core.Services;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Moving the library folder with a real database and temporary folders.</summary>
public sealed class LibraryMoverTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"tapedeck-move-{Guid.NewGuid():N}");
    private AppPaths paths = null!;
    private TrackRepository repository = null!;
    private LibraryMover mover = null!;

    private string OldDir => Path.Combine(this.root, "old");
    private string NewDir => Path.Combine(this.root, "new");

    public async Task InitializeAsync()
    {
        this.paths = new AppPaths(Path.Combine(this.root, "data"), this.OldDir);
        this.paths.EnsureDirectories();
        var factory = new SqliteConnectionFactory(this.paths.DatabasePath);
        await new Migrator(factory).MigrateAsync();
        this.repository = new TrackRepository(factory);
        this.mover = new LibraryMover(this.repository, this.paths);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.root, recursive: true);
        return Task.CompletedTask;
    }

    private async Task AddDownloadedAsync(string videoId, string fileName, bool createFile = true)
    {
        var id = await this.repository.AddAsync(videoId, videoId);
        await this.repository.UpdateDownloadStateAsync(id, DownloadStatus.Done, fileName, "opus");
        if (createFile)
            await File.WriteAllTextAsync(this.paths.ToLibraryPath(fileName), fileName);
    }

    [Fact]
    public async Task Moves_files_and_switches_the_library()
    {
        await this.AddDownloadedAsync("dQw4w9WgXcQ", "A - One.opus");
        await this.AddDownloadedAsync("9bZkp7q19f0", "B - Two.opus");
        await this.AddDownloadedAsync("kJQP7kiw5Fk", "C - Gone.opus", createFile: false);
        var reports = new List<(int, int)>();

        var moved = await this.mover.MoveAsync(this.NewDir, new SyncProgress(reports));

        Assert.Equal(2, moved);
        Assert.Equal(this.NewDir, this.paths.LibraryDir);
        Assert.True(File.Exists(Path.Combine(this.NewDir, "A - One.opus")));
        Assert.False(File.Exists(Path.Combine(this.OldDir, "A - One.opus")));
        Assert.Equal([(1, 2), (2, 2)], reports);
    }

    [Fact]
    public async Task The_own_lrc_files_move_along_and_the_users_ones_stay()
    {
        await this.AddDownloadedAsync("dQw4w9WgXcQ", "A - One.opus");
        await this.AddDownloadedAsync("9bZkp7q19f0", "B - Two.opus");
        LrcSidecar.Write(this.paths.ToLibraryPath("A - One.opus"), "[00:01.00] Line");
        await File.WriteAllTextAsync(this.paths.ToLibraryPath("B - Two.lrc"), "[00:01.00] mine");

        var moved = await this.mover.MoveAsync(this.NewDir);

        Assert.Equal(3, moved);
        Assert.True(LrcSidecar.IsOwn(Path.Combine(this.NewDir, "A - One.lrc")));
        Assert.True(File.Exists(Path.Combine(this.OldDir, "B - Two.lrc")));
        Assert.False(File.Exists(Path.Combine(this.NewDir, "B - Two.lrc")));
    }

    [Fact]
    public async Task Name_collision_stops_before_any_file_moves()
    {
        await this.AddDownloadedAsync("dQw4w9WgXcQ", "A - One.opus");
        await this.AddDownloadedAsync("9bZkp7q19f0", "B - Two.opus");
        Directory.CreateDirectory(this.NewDir);
        await File.WriteAllTextAsync(Path.Combine(this.NewDir, "B - Two.opus"), "other");

        var check = await this.mover.CheckAsync(this.NewDir);

        Assert.Equal(new MoveCheck(MoveProblem.NameTaken, "B - Two.opus"), check);
        await Assert.ThrowsAsync<InvalidOperationException>(() => this.mover.MoveAsync(this.NewDir));
        Assert.True(File.Exists(Path.Combine(this.OldDir, "A - One.opus")));
        Assert.Equal(this.OldDir, this.paths.LibraryDir);
    }

    [Fact]
    public async Task Same_or_nested_folder_is_rejected()
    {
        Assert.Equal(MoveProblem.SameFolder, (await this.mover.CheckAsync(this.OldDir + Path.DirectorySeparatorChar)).Problem);
        Assert.Equal(MoveProblem.InsideCurrentFolder, (await this.mover.CheckAsync(Path.Combine(this.OldDir, "sub"))).Problem);
    }

    /// <summary>Reports synchronously, unlike <see cref="Progress{T}"/> which posts to the thread pool.</summary>
    private sealed class SyncProgress(List<(int, int)> reports) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => reports.Add(value);
    }
}
