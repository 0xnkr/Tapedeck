using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tapedeck.Core.Playlists;

namespace Tapedeck.Services;

/// <summary>
/// Keeps the .m3u8 files in the library folder current. Changes ask for a sync with <see cref="Request"/>;
/// a short pause bundles bursts (a batch download, several playlist edits) into one write.
/// </summary>
public sealed class PlaylistSync(PlaylistExporter exporter)
{
    private static readonly TimeSpan DELAY = TimeSpan.FromSeconds(1);

    private CancellationTokenSource? pending;

    public void Request()
    {
        this.pending?.Cancel();
        var cts = this.pending = new CancellationTokenSource();
        _ = this.SyncAfterDelayAsync(cts);
    }

    /// <summary>Deletes the playlist files Tapedeck wrote into the old library folder after a move.</summary>
    public void RemoveExported(string folder) => exporter.RemoveExported(folder);

    /// <summary>Syncs right away, e.g. after the library folder moved.</summary>
    public async Task SyncNowAsync()
    {
        this.pending?.Cancel();
        this.pending = null;
        await TrySyncAsync(exporter);
    }

    private async Task SyncAfterDelayAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(DELAY, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (this.pending == cts)
                this.pending = null;
            cts.Dispose();
        }

        await TrySyncAsync(exporter);
    }

    private static async Task TrySyncAsync(PlaylistExporter exporter)
    {
        try
        {
            await exporter.SyncAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a locked file (e.g. open on a connected phone) is written again with the next change
        }
    }
}
