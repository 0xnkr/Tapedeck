using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core.Playlists;
using Tapedeck.Core.Services;

namespace Tapedeck.Core.Apple;

/// <summary>What exporting one track did.</summary>
public enum AppleExportOutcome
{
    /// <summary>The audio was encoded (new copy or new download).</summary>
    Encoded,

    /// <summary>Only the tags (and maybe the name) of the existing copy changed.</summary>
    Retagged,
}

/// <summary>
/// Keeps an <c>.m4a</c> copy of every downloaded track in the Apple folder. Works only from what is finished:
/// the Opus file and the values in the database. Nothing in the library changes, so the Opus logic
/// (<see cref="TagWriter"/>, renaming, the phone's playlists) stays as it is.
/// <list type="bullet">
/// <item>The copy is named like the Opus file and follows its renames.</item>
/// <item>New audio (another download) is encoded again; new values only rewrite the tags.</item>
/// <item>Only files recorded in <c>apple_exports</c> are Tapedeck's; others in the folder are never touched.</item>
/// </list>
/// </summary>
public sealed class AppleExporter(
    ITrackRepository tracks,
    IAppleExportRepository exports,
    IPlaylistRepository playlists,
    IAacEncoder encoder,
    AppPaths paths,
    ILogger<AppleExporter>? logger = null)
{
    public const string EXTENSION = ".m4a";

    // the playlist files written into the Apple folder, like exported-playlists.txt for the library
    private const string PLAYLIST_LIST = "exported-playlists-apple.txt";

    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    public bool IsAvailable => encoder.IsAvailable;

    /// <summary>Brings the copy of one track up to date.</summary>
    /// <exception cref="InvalidOperationException">The library file is missing or ffmpeg failed.</exception>
    public async Task<AppleExportOutcome> ExportAsync(AppleExportCandidate candidate, CancellationToken cancellation = default)
    {
        var data = await TrackTagData.LoadAsync(tracks, candidate.TrackId);
        var sourcePath = data.Track.FilePath!;
        var source = paths.ToLibraryPath(sourcePath);
        if (!File.Exists(source))
            throw new InvalidOperationException($"The library file {sourcePath} is missing.");

        Directory.CreateDirectory(paths.AppleExportDir);
        var existing = candidate.Existing;
        var existingPath = existing is null ? null : paths.ToAppleExportPath(existing.FilePath);
        var fileName = this.ChooseName(sourcePath, data.Track.VideoId, existing?.FilePath);
        var target = paths.ToAppleExportPath(fileName);

        var encode = existing is null
                     || existing.SourceDownloadedAt != candidate.DownloadedAt
                     || !File.Exists(existingPath);
        var started = System.Diagnostics.Stopwatch.StartNew();
        if (encode)
        {
            await encoder.EncodeAsync(source, target, cancellation);
            if (existingPath is not null && !SamePath(existingPath, target) && File.Exists(existingPath))
                File.Delete(existingPath);
        }
        else if (!SamePath(existingPath!, target))
        {
            File.Move(existingPath!, target);
        }

        await Task.Run(() => Mp4TagWriter.Write(target, data), cancellation);
        await exports.SaveAsync(new AppleExportRecord(candidate.TrackId, fileName, sourcePath, candidate.DownloadedAt, candidate.EditedAt));
        this.log.LogInformation("{Action} Apple copy {File} in {Ms} ms",
            encode ? "Encoded" : "Updated the tags of", fileName, started.ElapsedMilliseconds);
        return encode ? AppleExportOutcome.Encoded : AppleExportOutcome.Retagged;
    }

    /// <summary>Deletes the copies of tracks that were removed or lost their file.</summary>
    /// <returns>Number of copies deleted.</returns>
    public async Task<int> PruneAsync()
    {
        var orphans = await exports.ListOrphansAsync();
        foreach (var orphan in orphans)
        {
            var path = paths.ToAppleExportPath(orphan.FilePath);
            if (File.Exists(path))
                File.Delete(path);
            await exports.DeleteAsync(orphan.TrackId);
            this.log.LogInformation("Deleted the Apple copy {File}, its track is gone", orphan.FilePath);
        }
        return orphans.Count;
    }

    /// <summary>One <c>.m3u8</c> per playlist in the Apple folder, with the tracks that have a copy.</summary>
    public async Task<int> SyncPlaylistsAsync()
    {
        var copies = await exports.ListAsync();
        var exporter = new PlaylistExporter(playlists, paths);
        return await exporter.SyncAsync(new PlaylistTarget(paths.AppleExportDir, PLAYLIST_LIST,
            track => copies.TryGetValue(track.Id, out var copy) && File.Exists(paths.ToAppleExportPath(copy.FilePath))
                ? copy.FilePath
                : null));
    }

    /// <summary>
    /// Deletes all copies and playlists from <paramref name="folder"/> and forgets them, e.g. before the Apple folder
    /// changes; the next run exports everything into the new one.
    /// </summary>
    public async Task RemoveAllAsync(string folder)
    {
        foreach (var copy in (await exports.ListAsync()).Values)
        {
            var path = Path.Combine(folder, copy.FilePath);
            if (File.Exists(path))
                File.Delete(path);
        }
        new PlaylistExporter(playlists, paths).RemoveExported(folder, PLAYLIST_LIST);
        await exports.ClearAsync();
        this.log.LogInformation("Removed all Apple copies from {Folder}", folder);
    }

    // the Opus file's name with .m4a; with the video id when someone else's file already has that name
    private string ChooseName(string sourcePath, string videoId, string? ownName)
    {
        var plain = Path.GetFileNameWithoutExtension(sourcePath) + EXTENSION;
        if (IsFree(plain))
            return plain;
        return $"{Path.GetFileNameWithoutExtension(sourcePath)} [{videoId}]{EXTENSION}";

        bool IsFree(string name) =>
            string.Equals(name, ownName, StringComparison.OrdinalIgnoreCase) || !File.Exists(paths.ToAppleExportPath(name));
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.Ordinal);
}
