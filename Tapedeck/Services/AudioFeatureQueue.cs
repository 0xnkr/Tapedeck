using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tapedeck.Core;
using Tapedeck.Core.Audio;
using Tapedeck.Resources;

namespace Tapedeck.Services;

/// <summary>
/// Measures how downloaded files sound (tempo, timbre, energy) for the phone's recommendations, one after another
/// in the background: files from before the analysis existed, adopted files, and all of them again after a new
/// <see cref="AudioAnalysis.VERSION"/>. New downloads measure themselves. Each new value marks the tags as outdated,
/// so "Write tags" takes it to the file.
/// </summary>
public sealed class AudioFeatureQueue(
    AudioFeatureExtractor extractor,
    ITrackRepository repository,
    AppPaths paths,
    ActivityService activity,
    ILogger<AudioFeatureQueue> log)
{
    // rows are refreshed in groups, so a large library does not rebuild the table for every file
    private const int REPORT_EVERY = 25;

    // files that could not be measured are not tried again until the next start
    private readonly HashSet<long> failed = [];
    private bool running;
    private bool checkAgain;

    /// <summary>Tracks got a new sound value; their tags are outdated now.</summary>
    public event Action<IReadOnlyList<long>>? Measured;

    /// <summary>Starts measuring all downloaded tracks without a current value, unless a run is going already.</summary>
    public void EnqueueMissing()
    {
        if (this.running)
        {
            this.checkAgain = true;
            return;
        }
        if (!extractor.IsAvailable)
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
                var ids = (await repository.ListWithoutAudioFeaturesAsync(AudioAnalysis.VERSION))
                    .Where(id => !this.failed.Contains(id)).ToList();
                if (ids.Count > 0)
                    await this.MeasureAsync(ids);
            }
            while (this.checkAgain);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Sound analysis in the background stopped");
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
        using (var progress = activity.Begin(string.Format(Strings.Activity_AnalyzingSound, 0, ids.Count)))
        {
            for (var i = 0; i < ids.Count; i++)
            {
                progress.Update(string.Format(Strings.Activity_AnalyzingSound, i + 1, ids.Count));
                var id = ids[i];
                try
                {
                    // the file may have been removed meanwhile; one measured by a new download is simply measured again
                    if (await repository.GetAsync(id) is not { FilePath: { } file })
                        continue;
                    var features = await extractor.ExtractAsync(paths.ToLibraryPath(file));
                    await repository.SetAudioFeaturesAsync(id, features?.Format(), AudioAnalysis.VERSION);
                    measured.Add(id);
                    total++;
                }
                catch (InvalidOperationException ex)
                {
                    log.LogWarning("Could not measure the sound of track {TrackId}: {Error}", id, ex.Message);
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
        log.LogInformation("Measured the sound of {Measured} of {Count} tracks in the background", total, ids.Count);
        if (total > 0)
            activity.Confirm(string.Format(Strings.Confirm_SoundAnalyzed, total));
    }
}
