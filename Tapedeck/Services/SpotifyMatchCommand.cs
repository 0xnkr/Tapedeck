using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tapedeck.Core;
using Tapedeck.Core.Services;
using Tapedeck.Core.Settings;
using Tapedeck.Core.Spotify;
using Tapedeck.Data;

namespace Tapedeck.Services;

/// <summary>
/// <c>--spotify-match &lt;folder|zip&gt; [count]</c>: searches the first songs of the default selection that are not in
/// the library yet on YouTube, stores the results like the import does, and writes <c>spotify-matches.md</c> into the
/// history folder. For tuning the matching rules on real data; exits without opening a window.
/// </summary>
public static class SpotifyMatchCommand
{
    public const string FILE_NAME = "spotify-matches.md";
    public const int DEFAULT_COUNT = 100;

    /// <returns>The written report.</returns>
    public static async Task<string> RunAsync(string historyPath, int count, ILoggerFactory? logging)
    {
        var log = logging?.CreateLogger("SpotifyMatch");
        var settings = new SettingsStore(AppPaths.DefaultDataDir()).Load();
        var paths = AppPaths.Default(settings.LibraryDir);
        var factory = new SqliteConnectionFactory(paths.DatabasePath);
        await new Migrator(factory, logging?.CreateLogger<Migrator>()).MigrateAsync();
        var matches = new SpotifyMatchRepository(factory);

        var history = await new SpotifyHistoryReader(logging?.CreateLogger<SpotifyHistoryReader>()).ReadAsync(historyPath);
        var library = SpotifyLibraryMatch.TracksBySongKey(await new TrackRepository(factory).ListDisplayAsync(), await matches.ListAsync());
        var songs = SpotifySelection.DEFAULT.Apply(history.Streams)
            .Where(s => !library.ContainsKey(s.Key))
            .Take(count)
            .ToList();

        var metadata = new MetadataService(new ToolService(paths, logging?.CreateLogger<ToolService>()), logging?.CreateLogger<MetadataService>());
        var search = new SpotifySearchService(metadata, matches, logger: logging?.CreateLogger<SpotifySearchService>());
        var progress = new Progress<(int Done, int Total)>(p =>
        {
            if (p.Done % 10 == 0 && p.Done > 0)
                log?.LogInformation("Spotify search: {Done} of {Total}", p.Done, p.Total);
        });
        // a tuning run uses the current search, so earlier results without a decision are replaced
        await search.SearchAsync(songs, progress, again: true);

        var target = Path.Combine(Directory.Exists(historyPath) ? historyPath : Path.GetDirectoryName(Path.GetFullPath(historyPath))!, FILE_NAME);
        await File.WriteAllTextAsync(target, SpotifyMatchReport.ToMarkdown(songs, await matches.ListAsync()));
        log?.LogInformation("Spotify match report for {Songs} songs written to {Path}", songs.Count, target);
        return target;
    }
}
