using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tapedeck.Core;
using Tapedeck.Core.Audio;
using Tapedeck.Core.Csv;
using Tapedeck.Core.LastFm;
using Tapedeck.Core.Lyrics;
using Tapedeck.Core.MusicBrainz;
using Tapedeck.Core.Playlists;
using Tapedeck.Core.Services;
using Tapedeck.Core.Settings;
using Tapedeck.Core.Spotify;
using Tapedeck.Core.Updates;
using Tapedeck.Data;
using Tapedeck.Services;
using Tapedeck.Theme;
using Tapedeck.ViewModels;

namespace Tapedeck;

/// <summary>Composition root: wires backend, platform services and view models.</summary>
public static class AppServices
{
    /// <param name="configurePlatform">What the head registers for its platform, at least the <see cref="PlayerService"/>.</param>
    public static ServiceProvider Build(IAccentService accent, SettingsStore store, AppSettings settings,
        Action<IServiceCollection>? configurePlatform)
    {
        var paths = AppPaths.Default(settings.LibraryDir);
        paths.EnsureDirectories();

        var services = new ServiceCollection();

        // everything goes into the one log file; without it (designer, no write access) logging stays silent
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Information);
            if (FileLog.Current is { } file)
                logging.AddProvider(file);
        });

        services.AddSingleton(paths);
        services.AddSingleton(store);
        services.AddSingleton(new SqliteConnectionFactory(paths.DatabasePath));
        services.AddSingleton<Migrator>();
        services.AddSingleton<ITrackRepository, TrackRepository>();
        services.AddSingleton<IPlaylistRepository, PlaylistRepository>();
        services.AddSingleton<PlaylistExporter>();

        services.AddSingleton<HttpClient>();
        services.AddSingleton<MetadataService>();
        services.AddSingleton<ThumbnailService>();
        services.AddSingleton<TagWriter>();
        services.AddSingleton<LoudnessMeter>();
        services.AddSingleton<AudioFeatureExtractor>();
        services.AddSingleton<ThumbnailCoverService>();
        services.AddSingleton<DownloadService>();
        services.AddSingleton<TrackImportService>();
        services.AddSingleton<CsvImportService>();
        services.AddSingleton<PlaylistImportService>();
        services.AddSingleton<LibraryMover>();
        services.AddSingleton<MbClient>();
        services.AddSingleton<CoverArtService>();
        services.AddSingleton<MbEnrichmentService>();
        services.AddSingleton<ILastFmTags, LastFmClient>();
        services.AddSingleton<LastFmGenreService>();
        services.AddSingleton<ILyricsSource, LrclibClient>();
        services.AddSingleton<LyricsService>();
        services.AddSingleton<IYtSearch>(sp => sp.GetRequiredService<MetadataService>());
        services.AddSingleton<ISpotifyMatchRepository, SpotifyMatchRepository>();
        services.AddSingleton<SpotifySearchService>();
        services.AddSingleton<SpotifyImportService>();
        services.AddSingleton<SpotifyPlaylistClient>();
        services.AddSingleton<SpotifyTasteService>();
        services.AddSingleton<UpdateChecker>();

        services.AddSingleton(accent);
        services.AddSingleton<IPlatformService, PlatformService>();
        services.AddSingleton<ActivityService>();
        services.AddSingleton<ToolSetup>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<LibraryFiles>();
        services.AddSingleton<ExistingFileFinder>();
        services.AddSingleton<PlaylistSync>();
        services.AddSingleton<MusicBrainzQueue>();
        services.AddSingleton<LoudnessQueue>();
        services.AddSingleton<AudioFeatureQueue>();
        services.AddSingleton<CoverQueue>();
        services.AddSingleton<LastFmQueue>();
        services.AddSingleton<LyricsQueue>();

        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<PlaylistsViewModel>();
        // resolved on use: the playlist page needs the library and the playlists view models
        services.AddSingleton<PlaylistDetailFactory>(sp =>
            id => ActivatorUtilities.CreateInstance<PlaylistDetailViewModel>(sp, id));
        services.AddSingleton<SmartPlaylistEditorFactory>(sp =>
            id => ActivatorUtilities.CreateInstance<SmartPlaylistEditorViewModel>(sp, id));
        // one page that keeps its search running; resolved on use, it needs the playlists view model
        services.AddSingleton<SpotifyImportViewModel>();
        services.AddSingleton<SpotifyImportFactory>(sp => () => sp.GetRequiredService<SpotifyImportViewModel>());
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TrackDetailFactory>(sp => (row, actions, sequence) =>
        {
            var detail = ActivatorUtilities.CreateInstance<TrackDetailViewModel>(sp, row, actions, sequence);
            detail.LoadCommand.Execute(null);
            return detail;
        });

        configurePlatform?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
