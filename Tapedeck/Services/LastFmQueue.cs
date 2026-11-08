using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Tapedeck.Core.LastFm;
using Tapedeck.Core.Settings;
using Tapedeck.Resources;

namespace Tapedeck.Services;

/// <summary>
/// Asks Last.fm in the background for the genres of tracks MusicBrainz has none for. Runs only with the user's own
/// API key and waits for a running MusicBrainz lookup, which may still find genres of its own.
/// </summary>
public sealed partial class LastFmQueue(
    LastFmGenreService service,
    SettingsStore store,
    MusicBrainzQueue musicBrainz,
    ActivityService activity,
    ILogger<LastFmQueue> log) : ObservableObject
{
    private bool checkAgain;

    /// <summary>Tracks got genres from Last.fm; their tags are outdated now.</summary>
    public event Action<IReadOnlyList<long>>? Tagged;

    /// <summary>Last.fm refused the key or failed; the message is for a notice.</summary>
    public event Action<string>? Failed;

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    public bool HasKey => !string.IsNullOrWhiteSpace(store.Load().LastFmApiKey);

    /// <summary>Starts asking for all tracks without genres, unless a run is going already or there is no key.</summary>
    public void EnqueueMissing()
    {
        if (this.IsRunning)
        {
            this.checkAgain = true;
            return;
        }
        if (!this.HasKey || musicBrainz.IsRunning)
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
                if (store.Load().LastFmApiKey?.Trim() is not { Length: > 0 } key || await service.CountPendingAsync() == 0)
                    break;

                LastFmSummary summary;
                using (var progress = activity.Begin(string.Format(Strings.Activity_LastFmGenres, 0, 0)))
                {
                    summary = await service.FetchAsync(key, new Progress<(int Done, int Total)>(p =>
                        progress.Update(string.Format(Strings.Activity_LastFmGenres, p.Done, p.Total))));
                }

                if (summary.Tagged.Count > 0)
                    this.Tagged?.Invoke(summary.Tagged);
                if (summary.InvalidKey)
                {
                    this.Failed?.Invoke(Strings.Notice_LastFmInvalidKey);
                    break;
                }
                if (summary.Failed > 0)
                    this.Failed?.Invoke(string.Format(Strings.Notice_LastFmSomeFailed, summary.Failed));
            }
            while (this.checkAgain);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Last.fm genres stopped");
            this.Failed?.Invoke(string.Format(Strings.Notice_LastFmFailed, ex.Message));
        }
        finally
        {
            this.IsRunning = false;
        }
    }
}
