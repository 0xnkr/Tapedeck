using System.Diagnostics;
using Android.Media;
using Android.Provider;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core;
using AndroidApplication = Android.App.Application;

namespace Tapedeck.Android.Services;

/// <summary>
/// Keeps the phone's media index (MediaStore) in step with the library folder. Players find songs only through that
/// index, and Android does not add files an app with access to all files writes by path. So the folder is compared
/// with the index, and every file that is new, changed (tags written) or gone is handed to the media scanner.
/// Comparing instead of reporting each change keeps it independent of which code wrote the file.
/// </summary>
public sealed class MediaIndex(AppPaths paths, ILogger<MediaIndex>? logger = null)
{
    // a download is followed by tags, lyrics and the playlist files within seconds
    private static readonly TimeSpan DELAY = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SCAN_TIMEOUT = TimeSpan.FromMinutes(2);

    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? pending;
    // after moving the library the old folder's entries have to go as well
    private string? lastDir;

    /// <summary>Compares folder and index shortly, once things have settled.</summary>
    public void Request()
    {
        this.pending?.Cancel();
        var cts = this.pending = new CancellationTokenSource();
        _ = this.RunLaterAsync(cts.Token);
    }

    private async Task RunLaterAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(DELAY, ct);
            await this.gate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var dir = paths.LibraryDir;
            await Task.Run(() => this.SyncAsync(dir));
            if (this.lastDir is { } old && !string.Equals(old, dir, StringComparison.Ordinal))
                await Task.Run(() => this.SyncAsync(old));
            this.lastDir = dir;
        }
        catch (Exception ex)
        {
            this.log.LogWarning(ex, "Could not update the media index");
        }
        finally
        {
            this.gate.Release();
        }
    }

    private async Task SyncAsync(string dir)
    {
        var watch = Stopwatch.StartNew();
        // the index knows /storage/emulated/0/…, the library may be set as /sdcard/… (a link to it)
        var folder = new Java.IO.File(dir).CanonicalPath.TrimEnd('/');
        var indexed = Indexed(folder);

        var files = new Dictionary<string, FileInfo>(StringComparer.Ordinal);
        if (Directory.Exists(folder))
        {
            foreach (var path in Directory.EnumerateFiles(folder))
                files[path] = new FileInfo(path);
        }

        var changed = new List<string>();
        foreach (var (path, file) in files)
        {
            if (!indexed.TryGetValue(path, out var entry)
                || entry.Modified != new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeSeconds()
                || entry.Size != file.Length)
                changed.Add(path);
        }
        // the scanner drops entries whose file is gone
        changed.AddRange(indexed.Keys.Where(p => !files.ContainsKey(p)));
        if (changed.Count == 0)
            return;

        var scanned = await ScanAsync(changed);
        this.log.LogInformation("Media index: scanned {Scanned} of {Changed} changed files in {Folder} ({Milliseconds} ms)",
            scanned, changed.Count, folder, watch.ElapsedMilliseconds);
    }

    /// <summary>What the index knows about the files directly in the folder: modification time in seconds and size.</summary>
    private static Dictionary<string, (long Modified, long Size)> Indexed(string folder)
    {
        var result = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        var prefix = folder + "/";
        // LIKE treats _ and % as wildcards; the exact check below sorts out what does not belong
        var escaped = prefix.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        using var cursor = AndroidApplication.Context.ContentResolver!.Query(
            MediaStore.Files.GetContentUri("external")!,
            [MediaStore.IMediaColumns.Data, MediaStore.IMediaColumns.DateModified, MediaStore.IMediaColumns.Size],
            $"{MediaStore.IMediaColumns.Data} LIKE ? ESCAPE '\\'", [escaped + "%"], null);
        if (cursor is null)
            return result;

        while (cursor.MoveToNext())
        {
            var path = cursor.GetString(0);
            // only the files directly in the folder, like the library itself
            if (path is null || !path.StartsWith(prefix, StringComparison.Ordinal) || path.IndexOf('/', prefix.Length) >= 0)
                continue;
            result[path] = (cursor.GetLong(1), cursor.GetLong(2));
        }
        return result;
    }

    /// <returns>How many files the scanner answered for before the timeout.</returns>
    private static async Task<int> ScanAsync(IReadOnlyList<string> files)
    {
        var listener = new ScanListener(files.Count);
        MediaScannerConnection.ScanFile(AndroidApplication.Context, files.ToArray(), null, listener);
        await Task.WhenAny(listener.Done, Task.Delay(SCAN_TIMEOUT));
        return listener.Count;
    }

    private sealed class ScanListener(int expected) : Java.Lang.Object, MediaScannerConnection.IOnScanCompletedListener
    {
        private readonly TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int count;

        public Task Done => this.done.Task;

        public int Count => Volatile.Read(ref this.count);

        public void OnScanCompleted(string? path, global::Android.Net.Uri? uri)
        {
            if (Interlocked.Increment(ref this.count) >= expected)
                this.done.TrySetResult();
        }
    }
}
