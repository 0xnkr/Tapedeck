using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tapedeck.Core;
using Tapedeck.Core.Spotify;
using Tapedeck.Data;

namespace Tapedeck.Services;

/// <summary>
/// <c>--spotify-report &lt;folder|zip&gt;</c>: writes <c>spotify-report.md</c> into the history folder (next to the
/// zip) and exits without opening a window. The library is only read, to mark songs that are already in it.
/// </summary>
public static class SpotifyReportCommand
{
    public const string FILE_NAME = "spotify-report.md";

    /// <returns>The written report.</returns>
    public static async Task<string> RunAsync(string historyPath, ILoggerFactory? logging)
    {
        var log = logging?.CreateLogger("SpotifyReport");
        var history = await new SpotifyHistoryReader(logging?.CreateLogger<SpotifyHistoryReader>()).ReadAsync(historyPath);

        var library = await ReadLibraryAsync(log);
        var target = Path.Combine(Directory.Exists(historyPath) ? historyPath : Path.GetDirectoryName(Path.GetFullPath(historyPath))!, FILE_NAME);
        await File.WriteAllTextAsync(target, SpotifyReport.ToMarkdown(history, SpotifyPlayRules.DEFAULT, library));
        // no song names: the history is private
        log?.LogInformation("Spotify report with {Streams} plays written to {Path}", history.Streams.Count, target);
        return target;
    }

    private static async Task<IReadOnlyDictionary<string, long>?> ReadLibraryAsync(ILogger? log)
    {
        var database = Path.Combine(AppPaths.DefaultDataDir(), "tapedeck.db");
        if (!File.Exists(database))
            return null;
        try
        {
            var tracks = await new TrackRepository(new SqliteConnectionFactory(database)).ListDisplayAsync();
            return SpotifyLibraryMatch.TracksBySongKey(tracks);
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Spotify report without library, the database could not be read");
            return null;
        }
    }
}
