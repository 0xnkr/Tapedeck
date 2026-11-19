using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tapedeck.Core;
using Tapedeck.Core.Audio;
using Tapedeck.Core.Services;
using Tapedeck.Core.Settings;
using Tapedeck.Data;

namespace Tapedeck.Services;

/// <summary>
/// <c>--audio-report [count]</c>: measures the sound of every downloaded track that has no current value yet (stored
/// like the app does it, so it need not measure them again), then writes <c>audio-report.md</c> next to the database
/// and exits without opening a window. For tuning the sound analysis on the real library.
/// </summary>
public static class AudioReportCommand
{
    public const string FILE_NAME = "audio-report.md";
    public const int DEFAULT_COUNT = 20;

    // progress in the log without a line per file
    private const int LOG_EVERY = 25;

    /// <returns>The written report.</returns>
    public static async Task<string> RunAsync(int count, ILoggerFactory? logging)
    {
        var log = logging?.CreateLogger("AudioReport");
        var settings = new SettingsStore(AppPaths.DefaultDataDir()).Load();
        var paths = AppPaths.Default(settings.LibraryDir);
        var factory = new SqliteConnectionFactory(paths.DatabasePath);
        await new Migrator(factory, logging?.CreateLogger<Migrator>()).MigrateAsync();
        var repository = new TrackRepository(factory);
        var extractor = new AudioFeatureExtractor(new ToolService(paths, logging?.CreateLogger<ToolService>()));
        if (!extractor.IsAvailable)
            throw new InvalidOperationException("ffmpeg is missing; start Tapedeck once so it installs its tools.");

        var missing = await repository.ListWithoutAudioFeaturesAsync(AudioAnalysis.VERSION);
        var started = DateTime.UtcNow;
        for (var i = 0; i < missing.Count; i++)
        {
            var id = missing[i];
            try
            {
                if (await repository.GetAsync(id) is not { FilePath: { } file })
                    continue;
                var features = await extractor.ExtractAsync(paths.ToLibraryPath(file));
                await repository.SetAudioFeaturesAsync(id, features?.Format(), AudioAnalysis.VERSION);
            }
            catch (InvalidOperationException ex)
            {
                log?.LogWarning("Could not measure the sound of track {TrackId}: {Error}", id, ex.Message);
            }
            if ((i + 1) % LOG_EVERY == 0)
                log?.LogInformation("Sound analysis: {Done} of {Total}", i + 1, missing.Count);
        }
        if (missing.Count > 0)
            log?.LogInformation("Measured the sound of {Count} tracks in {Seconds:0} s ({PerTrack:0.00} s per track)", missing.Count,
                (DateTime.UtcNow - started).TotalSeconds, (DateTime.UtcNow - started).TotalSeconds / missing.Count);

        var entries = (await repository.ListAudioFeaturesAsync())
            .Select(r => AudioFeatures.Parse(r.Features) is { } features ? new AudioReportEntry(r.Id, r.Artist, r.Title, features) : null)
            .OfType<AudioReportEntry>()
            .ToList();
        var target = Path.Combine(paths.DataDir, FILE_NAME);
        await File.WriteAllTextAsync(target, AudioReport.ToMarkdown(entries, count));
        log?.LogInformation("Sound report for {Tracks} tracks written to {Path}", entries.Count, target);
        return target;
    }
}
