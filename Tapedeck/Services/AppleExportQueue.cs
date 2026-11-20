using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Tapedeck.Core;
using Tapedeck.Core.Apple;
using Tapedeck.Core.Settings;
using Tapedeck.Resources;

namespace Tapedeck.Services;

/// <summary>
/// Keeps the Apple folder in step with the library while the target system is Apple. A run starts only once
/// Tapedeck has nothing else to do (no download, no background lookup), so the copies get the finished values
/// instead of being rewritten after every step. New downloads are encoded, changed values only retag.
/// </summary>
public sealed partial class AppleExportQueue : ObservableObject
{
    // how long nothing else must run before the export starts
    private static readonly TimeSpan QUIET = TimeSpan.FromSeconds(5);

    // the counter in the toolbar is updated this often during a run
    private const int REPORT_EVERY = 10;

    private readonly AppleExporter exporter;
    private readonly IAppleExportRepository exports;
    private readonly SettingsStore settings;
    private readonly AppPaths paths;
    private readonly ActivityService activity;
    private readonly ILogger<AppleExportQueue> log;

    // tracks that failed are not tried again until the next start or "Export now"
    private readonly HashSet<long> failed = [];
    private bool active;
    private bool requested;
    private bool now;

    public AppleExportQueue(AppleExporter exporter, IAppleExportRepository exports, SettingsStore settings, AppPaths paths,
        ActivityService activity, ILogger<AppleExportQueue> log)
    {
        this.exporter = exporter;
        this.exports = exports;
        this.settings = settings;
        this.paths = paths;
        this.activity = activity;
        this.log = log;
        this.IsEnabled = TargetSystemCode.Parse(settings.Load().TargetSystem) == TargetSystem.Apple;
        // whatever just finished (a download, a lookup, a correction saved on the detail page) may have changed values
        activity.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActivityService.IsBusy) && !activity.IsBusy)
                this.Request();
        };
    }

    /// <summary>The target system is Apple; the toolbar shows the state of the copies.</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(DetailText))]
    public partial bool IsRunning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(DetailText), nameof(IsCurrent))]
    public partial AppleExportCounts Counts { get; private set; } = new(0, 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    public partial int FailedCount { get; private set; }

    /// <summary>Every downloaded track has an up-to-date copy.</summary>
    public bool IsCurrent => this.Counts.Pending == 0;

    /// <summary>Short state for the toolbar pill, e.g. "Apple: 412/450".</summary>
    public string StatusText => this.Counts.Pending == 0
        ? Strings.Apple_StatusCurrent
        : string.Format(Strings.Apple_Status, this.Counts.Current, this.Counts.Total);

    /// <summary>The longer state in the pill's flyout and in the settings.</summary>
    public string DetailText
    {
        get
        {
            var text = this.IsRunning ? string.Format(Strings.Apple_DetailRunning, this.Counts.Current, this.Counts.Total)
                : this.Counts.Pending == 0 ? string.Format(Strings.Apple_DetailCurrent, this.Counts.Total)
                : string.Format(Strings.Apple_DetailWaiting, this.Counts.Current, this.Counts.Total);
            return this.FailedCount > 0 ? text + " " + string.Format(Strings.Apple_DetailFailed, this.FailedCount) : text;
        }
    }

    public string ExportDir => this.paths.AppleExportDir;

    /// <summary>Asks for a run once Tapedeck is quiet; called whenever tracks or their values change.</summary>
    public void Request()
    {
        if (!this.IsEnabled)
            return;
        this.requested = true;
        if (!this.active)
            _ = this.RunAsync();
    }

    /// <summary>Runs right away and tries failed tracks again.</summary>
    public void ExportNow()
    {
        if (!this.IsEnabled)
            return;
        this.failed.Clear();
        this.FailedCount = 0;
        this.now = true;
        this.Request();
    }

    /// <summary>Reads the target system again after the settings changed it.</summary>
    public async Task ApplySettingsAsync()
    {
        this.IsEnabled = TargetSystemCode.Parse(this.settings.Load().TargetSystem) == TargetSystem.Apple;
        this.OnPropertyChanged(nameof(this.ExportDir));
        await this.UpdateCountsAsync();
        this.Request();
    }

    /// <summary>Moves the export to another folder: the copies in the old one are deleted, the next run fills the new one.</summary>
    /// <exception cref="InvalidOperationException">A run is going.</exception>
    public async Task ChangeFolderAsync(string folder)
    {
        if (this.active)
            throw new InvalidOperationException("The Apple export is running.");
        var old = this.paths.AppleExportDir;
        await this.exporter.RemoveAllAsync(old);
        this.paths.ChangeAppleExportDir(folder);
        this.settings.Update(s => s with { AppleExportDir = folder });
        this.log.LogInformation("Moved the Apple export from {Old} to {New}", old, folder);
        await this.ApplySettingsAsync();
    }

    /// <summary>Whether the folder can be changed now (no run going).</summary>
    public bool CanChangeFolder => !this.active;

    public async Task UpdateCountsAsync()
    {
        try
        {
            this.Counts = await this.exports.CountAsync();
        }
        catch (Exception ex)
        {
            this.log.LogWarning("Could not count the Apple copies: {Error}", ex.Message);
        }
    }

    private async Task RunAsync()
    {
        this.active = true;
        this.OnPropertyChanged(nameof(this.CanChangeFolder));
        try
        {
            await this.UpdateCountsAsync();
            while (this.requested && this.IsEnabled)
            {
                this.requested = false;
                if (!this.now)
                {
                    // until downloads and lookups are done, so the copies get the finished values
                    do
                        await Task.Delay(QUIET);
                    while (this.activity.IsBusy);
                }
                this.now = false;
                if (!this.IsEnabled || !this.exporter.IsAvailable)
                    break;
                await this.RunOnceAsync();
            }
        }
        catch (Exception ex)
        {
            this.log.LogError(ex, "The Apple export stopped");
        }
        finally
        {
            this.active = false;
            this.IsRunning = false;
            this.OnPropertyChanged(nameof(this.CanChangeFolder));
            await this.UpdateCountsAsync();
        }
    }

    private async Task RunOnceAsync()
    {
        var pruned = await this.exporter.PruneAsync();
        var pending = (await this.exports.ListPendingAsync()).Where(c => !this.failed.Contains(c.TrackId)).ToList();
        var encoded = 0;
        var retagged = 0;
        if (pending.Count > 0)
        {
            this.IsRunning = true;
            using var progress = this.activity.Begin(string.Format(Strings.Activity_AppleExport, 0, pending.Count));
            for (var i = 0; i < pending.Count; i++)
            {
                if (!this.IsEnabled)
                    break;
                progress.Update(string.Format(Strings.Activity_AppleExport, i + 1, pending.Count));
                try
                {
                    if (await this.exporter.ExportAsync(pending[i]) == AppleExportOutcome.Encoded)
                        encoded++;
                    else
                        retagged++;
                }
                catch (Exception ex)
                {
                    this.log.LogWarning("Could not export track {TrackId} for Apple: {Error}", pending[i].TrackId, ex.Message);
                    this.failed.Add(pending[i].TrackId);
                    this.FailedCount = this.failed.Count;
                }

                if ((i + 1) % REPORT_EVERY == 0)
                    await this.UpdateCountsAsync();
            }
            this.IsRunning = false;
        }

        if (pending.Count > 0 || pruned > 0)
        {
            try
            {
                await this.exporter.SyncPlaylistsAsync();
            }
            catch (Exception ex)
            {
                this.log.LogWarning("Could not write the Apple playlists: {Error}", ex.Message);
            }
        }

        if (pending.Count == 0 && pruned == 0)
            return;
        this.log.LogInformation("Apple export: {Encoded} encoded, {Retagged} retagged, {Pruned} removed, {Failed} failed",
            encoded, retagged, pruned, this.failed.Count);
        if (encoded + retagged > 0)
            this.activity.Confirm(string.Format(Strings.Confirm_AppleExported, encoded + retagged));
    }
}
