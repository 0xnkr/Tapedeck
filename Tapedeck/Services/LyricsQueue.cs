using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Tapedeck.Core.Lyrics;
using Tapedeck.Core.Settings;
using Tapedeck.Resources;

namespace Tapedeck.Services;

/// <summary>
/// Asks LRCLIB in the background for the lyrics of downloaded tracks. Waits for a running MusicBrainz lookup,
/// whose names find lyrics more often. After a run with failures it waits for the next start or the button in the
/// settings, so a service that is down is not asked again on every change.
/// </summary>
public sealed partial class LyricsQueue(
    LyricsService service,
    SettingsStore store,
    MusicBrainzQueue musicBrainz,
    ActivityService activity,
    ILogger<LyricsQueue> log) : ObservableObject
{
    private bool checkAgain;
    private bool paused;

    /// <summary>The stored lyrics of these tracks changed; their tags are outdated now.</summary>
    public event Action<IReadOnlyList<long>>? Changed;

    /// <summary>LRCLIB failed; the message is for a notice.</summary>
    public event Action<string>? Failed;

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    /// <summary>Starts asking for all tracks without lyrics, unless it is switched off or a run is going already.</summary>
    public void EnqueueMissing()
    {
        if (!(store.Load().LyricsAuto ?? true) || this.paused)
            return;
        this.Enqueue();
    }

    /// <summary>Asks for all tracks without lyrics now, also when automatic lookups are off or paused.</summary>
    public void EnqueueNow()
    {
        this.paused = false;
        this.Enqueue();
    }

    private void Enqueue()
    {
        if (this.IsRunning)
        {
            this.checkAgain = true;
            return;
        }
        if (musicBrainz.IsRunning)
            return;
        _ = this.RunAsync();
    }

    private async Task RunAsync()
    {
        this.IsRunning = true;
        try
        {
            do
            {
                this.checkAgain = false;
                if (await service.CountPendingAsync() == 0)
                    break;

                LyricsSummary summary;
                using (var progress = activity.Begin(string.Format(Strings.Activity_Lyrics, 0, 0)))
                {
                    summary = await service.FetchMissingAsync(new Progress<(int Done, int Total)>(p =>
                        progress.Update(string.Format(Strings.Activity_Lyrics, p.Done, p.Total))));
                }

                if (summary.Changed.Count > 0)
                    this.Changed?.Invoke(summary.Changed);
                if (summary.Failed > 0)
                {
                    this.paused = true;
                    this.Failed?.Invoke(string.Format(Strings.Notice_LyricsSomeFailed, summary.Failed));
                    break;
                }
            }
            while (this.checkAgain);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "LRCLIB lyrics stopped");
            this.paused = true;
            this.Failed?.Invoke(string.Format(Strings.Notice_LyricsFailed, ex.Message));
        }
        finally
        {
            this.IsRunning = false;
        }
    }
}
