using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.Services;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Missing files, renaming after corrections and removing tracks, with a real database and temp folders.</summary>
public sealed class LibraryFilesTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"tapedeck-files-{Guid.NewGuid():N}");
    private AppPaths paths = null!;
    private TrackRepository repository = null!;
    private LibraryFiles files = null!;

    public async Task InitializeAsync()
    {
        this.paths = new AppPaths(Path.Combine(this.root, "data"), Path.Combine(this.root, "library"));
        this.paths.EnsureDirectories();
        var factory = new SqliteConnectionFactory(this.paths.DatabasePath);
        await new Migrator(factory).MigrateAsync();
        this.repository = new TrackRepository(factory);
        this.files = new LibraryFiles(this.repository, this.paths);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.root, recursive: true);
        return Task.CompletedTask;
    }

    private async Task<long> AddDownloadedAsync(string fileName, string artist = "Rick Astley", string title = "Never Gonna Give You Up")
    {
        var id = await this.repository.AddAsync("dQw4w9WgXcQ", "u");
        await this.repository.UpdateYtMetadataAsync(id, new YtMetadata("dQw4w9WgXcQ", $"{artist} - {title}", "Channel",
            "20091025", 213, title, artist, null, null, artist, title, [], "{}"));
        await this.repository.UpdateDownloadStateAsync(id, DownloadStatus.Done, fileName, "opus");
        await File.WriteAllTextAsync(this.paths.ToLibraryPath(fileName), "audio");
        return id;
    }

    [Fact]
    public async Task Done_track_without_file_is_missing()
    {
        var id = await this.AddDownloadedAsync("Rick Astley - Never Gonna Give You Up.opus");
        Assert.Equal(DownloadStatus.Done, this.files.StatusOf((await this.repository.GetDisplayAsync(id))!));

        File.Delete(this.paths.ToLibraryPath("Rick Astley - Never Gonna Give You Up.opus"));
        Assert.Equal(DownloadStatus.Missing, this.files.StatusOf((await this.repository.GetDisplayAsync(id))!));
    }

    [Fact]
    public async Task Correction_renames_the_file()
    {
        var id = await this.AddDownloadedAsync("Rick Astley - Never Gonna Give You Up.opus");
        Assert.Null(await this.files.RenameToMatchAsync(id));

        await this.repository.SetUserFieldsAsync(id, new UserFields("Rick", "NGGYU", null, null, null, null));
        Assert.Equal("Rick - NGGYU.opus", await this.files.RenameToMatchAsync(id));

        Assert.True(File.Exists(this.paths.ToLibraryPath("Rick - NGGYU.opus")));
        Assert.False(File.Exists(this.paths.ToLibraryPath("Rick Astley - Never Gonna Give You Up.opus")));
        Assert.Equal("Rick - NGGYU.opus", (await this.repository.GetDisplayAsync(id))!.FilePath);
    }

    [Fact]
    public async Task Rename_avoids_other_files()
    {
        var id = await this.AddDownloadedAsync("Rick Astley - Never Gonna Give You Up.opus");
        await File.WriteAllTextAsync(this.paths.ToLibraryPath("Rick - NGGYU.opus"), "someone else");

        await this.repository.SetUserFieldsAsync(id, new UserFields("Rick", "NGGYU", null, null, null, null));
        Assert.Equal("Rick - NGGYU [dQw4w9WgXcQ].opus", await this.files.RenameToMatchAsync(id));
        Assert.Equal("someone else", await File.ReadAllTextAsync(this.paths.ToLibraryPath("Rick - NGGYU.opus")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Remove_deletes_the_track_and_optionally_the_file(bool deleteFile)
    {
        var id = await this.AddDownloadedAsync("Rick Astley - Never Gonna Give You Up.opus");
        await this.repository.AddTagAsync(id, "Pop", TagSource.User);

        await this.files.RemoveAsync(id, deleteFile);

        Assert.Null(await this.repository.GetDisplayAsync(id));
        Assert.Equal(!deleteFile, File.Exists(this.paths.ToLibraryPath("Rick Astley - Never Gonna Give You Up.opus")));
    }

    [Fact]
    public void Corrected_names_replace_the_old_file_name()
    {
        Assert.Equal("Rick - NGGYU.opus",
            FileNaming.ChooseForTrack("Rick Astley - Never Gonna Give You Up.opus", "Rick", "NGGYU", "dQw4w9WgXcQ", "opus", _ => false));
        // the old file is taken by the track itself, so it never forces the [id] variant
        Assert.Equal("Rick Astley - Never Gonna Give You Up.opus",
            FileNaming.ChooseForTrack("Rick Astley - Old.opus", "Rick Astley", "Never Gonna Give You Up", "dQw4w9WgXcQ", "opus",
                name => name == "Rick Astley - Old.opus"));
    }
}
