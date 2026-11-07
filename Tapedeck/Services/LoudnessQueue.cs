using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tapedeck.Core;
using Tapedeck.Core.Services;
using Tapedeck.Resources;

namespace Tapedeck.Services;

/// <summary>
/// Measures the loudness of downloaded files that have no value yet, one after another in the background:
/// downloads from before the measurement existed and files adopted from the library folder. New downloads
/// measure themselves. Each new value marks the tags as outdated, so "Write tags" takes the gain to the file.
/// </summary>
public sealed class LoudnessQueue(
    LoudnessMeter meter,
    ITrackRepository repository,
    AppPaths paths,
    ActivityService activity,
    ILogger<LoudnessQueue> log)
{
    // rows are refreshed in groups, so a large library does not rebuild the table for every file
    private const int REPORT_EVERY = 25;

    // files that could not be measured are not tried again until the next start
    private readonly HashSet<long> failed = [];
    private bool running;
    private bool checkAgain;

    /// <summary>Tracks got a loudness value; their tags are outdated now.</summary>
    public event Action<IReadOnlyList<long>>? Measured;

    /// <summary>Starts measuring all downloaded tracks without a value, unless a run is going already.</summary>
    public void EnqueueMissing()
    {
        if (this.running)
        {
            this.checkAgain = true;
            return;
        }
        if (!meter.IsAvailable)
            return;
        _ = this.RunAsync();
    }

    private async Task RunAsync()
    {
        this.running = true;
        try
        {
            do
            {
                this.checkAgain = false;
                var ids = (await repository.ListWithoutLoudnessAsync()).Where(id => !this.failed.Contains(id)).ToList();
                if (ids.Count > 0)
                    await this.MeasureAsync(ids);
            }
            while (this.checkAgain);
        }
        finally
        {
            this.running = false;
        }
    }

    private async Task MeasureAsync(List<long> ids)
    {
        var measured = new List<long>();
        var total = 0;
        using (var progress = activity.Begin(string.Format(Strings.Activity_MeasuringLoudness, 0, ids.Count)))
        {
            for (var i = 0; i < ids.Count; i++)
            {
                progress.Update(string.Format(Strings.Activity_MeasuringLoudness, i + 1, ids.Count));
                var id = ids[i];
                try
                {
                    // the file may have been removed or replaced by a download meanwhile
                    if (await repository.GetAsync(id) is not { FilePath: { } file } || await repository.GetLoudnessAsync(id) is not null)
                        continue;
                    await repository.SetLoudnessAsync(id, await meter.MeasureAsync(paths.ToLibraryPath(file)));
                    measured.Add(id);
                    total++;
                }
                catch (InvalidOperationException ex)
                {
                    log.LogWarning("Could not measure the loudness of track {TrackId}: {Error}", id, ex.Message);
                    this.failed.Add(id);
                }

                if (measured.Count >= REPORT_EVERY)
                {
                    this.Measured?.Invoke(measured);
                    measured = [];
                }
            }
        }

        if (measured.Count > 0)
            this.Measured?.Invoke(measured);
        log.LogInformation("Measured the loudness of {Measured} of {Count} tracks in the background", total, ids.Count);
        if (total > 0)
            activity.Confirm(string.Format(Strings.Confirm_LoudnessMeasured, total));
    }
}
