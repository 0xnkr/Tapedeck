using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Tapedeck.Core;
using Tapedeck.Core.Services;
using Tapedeck.Data;
using Tapedeck.Services;
using Tapedeck.Theme;
using Tapedeck.ViewModels;

namespace Tapedeck;

/// <summary>Composition root: wires backend, platform services and view models.</summary>
public static class AppServices
{
    public static ServiceProvider Build(IAccentService accent)
    {
        var paths = AppPaths.Default();
        paths.EnsureDirectories();

        var services = new ServiceCollection();

        services.AddSingleton(paths);
        services.AddSingleton(new SqliteConnectionFactory(paths.DatabasePath));
        services.AddSingleton<Migrator>();
        services.AddSingleton<ITrackRepository, TrackRepository>();

        services.AddSingleton<HttpClient>();
        services.AddSingleton<ToolService>();
        services.AddSingleton<MetadataService>();
        services.AddSingleton<ThumbnailService>();
        services.AddSingleton<TagWriter>();
        services.AddSingleton<DownloadService>();
        services.AddSingleton<TrackImportService>();

        services.AddSingleton(accent);
        services.AddSingleton<IPlatformService, PlatformService>();
        services.AddSingleton<ActivityService>();
        services.AddSingleton<NavigationService>();

        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TrackDetailFactory>(sp => (row, actions) =>
        {
            var detail = ActivatorUtilities.CreateInstance<TrackDetailViewModel>(sp, row, actions);
            detail.LoadCommand.Execute(null);
            return detail;
        });

        return services.BuildServiceProvider();
    }
}
