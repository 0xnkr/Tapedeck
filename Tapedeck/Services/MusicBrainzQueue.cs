using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.Services;
using Tapedeck.Core.Settings;
using Tapedeck.Resources;

namespace Tapedeck.Services;

/// <summary>
/// Looks up tracks on MusicBrainz one after another in the background, for the bulk action and after imports.
/// Imports only queue tracks never searched; "match all" searches every track the user did not decline
/// and keeps existing matches. The detail page searches a single track directly.
/// MusicBrainz allows one request per second, so a large library takes a while: the toast shows the progress.
/// </summary>
public sealed partial class MusicBrainzQueue(
    MbEnrichmentService enrichment,
    ITrackRepository repository,
    ActivityService activity,
    SettingsStore settings,
    ILogger<MusicBrainzQueue> log) : ObservableObject
{
    private const int MAX_FAILURES_IN_A_ROW = 3;

    // per track: search it again although it was searched before
    private readonly Queue<(long Id, bool All)> pending = new();
    private readonly HashSet<long> queued = [];
    private CancellationTokenSource? run;
    private int total;

    /// <summary>A track's MusicBrainz state or values changed.</summary>
    public event Action<long>? TrackChanged;

    /// <summary>A run stopped because MusicBrainz could not be reached; the text says why.</summary>
    public event Action<string>? Failed;

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    /// <summary>The setting "match new tracks automatically", on unless switched off.</summary>
    public bool AutoMatch => settings.Load().MusicBrainzAuto ?? true;

    /// <summary>Adds tracks to the running run, or starts one.</summary>
    /// <param name="all">
    /// Search them also when searched before, except the ones the user declined; matches stay when nothing certain is found.
    /// Without, only tracks never searched are looked up.
    /// </param>
    public void Enqueue(IEnumerable<long> trackIds, bool all = false)
    {
        foreach (var id in trackIds)
        {
            if (!this.queued.Add(id))
                continue;
            this.pending.Enqueue((id, all));
            this.total++;
        }

        if (!this.IsRunning && this.pending.Count > 0)
            _ = this.RunAsync();
    }

    /// <summary>Queues the tracks of an import when the user wants new tracks matched automatically.</summary>
    public void EnqueueImported(IEnumerable<long> trackIds)
    {
        if (this.AutoMatch)
            this.Enqueue(trackIds);
    }

    /// <summary>Stops after the current track; the rest stays unsearched.</summary>
    public void Cancel() => this.run?.Cancel();

    private async Task RunAsync()
    {
        this.IsRunning = true;
        using var cts = this.run = new CancellationTokenSource();
        var done = 0;
        var matched = 0;
        var suggested = 0;
        var notFound = 0;
        var failures = 0;
        string? error = null;

        using (var progress = activity.Begin(string.Format(Strings.Activity_MatchingMusicBrainz, 0, this.total)))
        {
            while (this.pending.TryDequeue(out var entry))
            {
                if (cts.IsCancellationRequested)
                    break;
                progress.Update(string.Format(Strings.Activity_MatchingMusicBrainz, done + 1, this.total));

                try
                {
                    // imports queue known tracks too; those keep their match. A declined track stays declined
                    var id = entry.Id;
                    var before = (await repository.GetAsync(id))?.MbState;
                    var search = entry.All ? before is not null and not MbMatchState.Rejected : before == MbMatchState.None;
                    if (search)
                    {
                        var state = await enrichment.EnrichAsync(id, keepMatch: entry.All, cts.Token);
                        matched += state == MbMatchState.Matched ? 1 : 0;
                        suggested += state == MbMatchState.Suggested ? 1 : 0;
                        notFound += state == MbMatchState.NoMatch ? 1 : 0;
                        this.TrackChanged?.Invoke(id);
                    }
                    failures = 0;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpRequestException ex)
                {
                    log.LogWarning("MusicBrainz search for track {TrackId} failed: {Error}", entry.Id, ex.Message);
                    // a busy server can fail a single track; it stays unsearched for the next run.
                    // Several in a row mean offline or MusicBrainz down: the rest would fail the same way
                    error = ex.Message;
                    if (++failures >= MAX_FAILURES_IN_A_ROW)
                        break;
                }
                catch (InvalidOperationException)
                {
                    // removed while waiting in the queue
                }
                done++;
            }
        }

        this.pending.Clear();
        this.queued.Clear();
        this.total = 0;
        this.run = null;
        this.IsRunning = false;

        log.LogInformation("MusicBrainz run ended after {Done} tracks: {Matched} matched, {Suggested} suggested, {NotFound} not found",
            done, matched, suggested, notFound);
        // single failed tracks just stay unsearched; only a stopped run is worth a notice
        if (failures >= MAX_FAILURES_IN_A_ROW && error is not null)
            this.Failed?.Invoke(error);
        else if (matched + suggested + notFound > 0)
            activity.Confirm(string.Format(Strings.Confirm_MatchedMusicBrainz, matched, suggested, notFound));
    }
}
