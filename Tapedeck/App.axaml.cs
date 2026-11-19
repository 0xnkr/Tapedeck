using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tapedeck.Core;
using Tapedeck.Core.Settings;
using Tapedeck.Data;
using Tapedeck.Services;
using Tapedeck.Theme;
using Tapedeck.ViewModels;
using Tapedeck.Views;

namespace Tapedeck;

public partial class App : Application
{
    private AccentService accent = null!;

    /// <summary>Accent for this run only (the desktop's <c>--accent</c>); wins over the saved one.</summary>
    public Color? StartupAccent { get; init; }

    /// <summary>
    /// Registers what each platform does its own way, set by the head that starts the app.
    /// It has to register at least the <see cref="PlayerService"/>.
    /// </summary>
    public Action<IServiceCollection>? ConfigurePlatform { get; init; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Before any window exists, so the accent resources are there for the first layout and the previewer
        this.accent = new AccentService(this.Resources);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        switch (this.ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
            {
                var (services, main) = this.Start();
                var log = services.GetRequiredService<ILogger<App>>();
                desktop.MainWindow = new MainWindow { DataContext = main };
                Load(main);
                desktop.Exit += (_, e) =>
                {
                    log.LogInformation("Tapedeck closed (exit code {ExitCode})", e.ApplicationExitCode);
                    services.Dispose();
                };
                break;
            }
            case ISingleViewApplicationLifetime singleView:
            {
                var (_, main) = this.Start();
                singleView.MainView = new MainView { DataContext = main };
                Load(main);
                break;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Settings, accent, services and database.</summary>
    private (ServiceProvider Services, MainViewModel Main) Start()
    {
        var store = new SettingsStore(AppPaths.DefaultDataDir());
        var settings = store.Load();
        // --accent for this run, else the saved accent, else the palette
        if (this.StartupAccent is { } startupAccent)
            this.accent.Apply(startupAccent);
        else if (settings.Accent is { } savedAccent)
            this.accent.TryApply(savedAccent);

        var services = AppServices.Build(this.accent, store, settings, this.ConfigurePlatform);
        var log = services.GetRequiredService<ILogger<App>>();
        // errors in UI code (commands, bindings, event handlers) that nobody caught
        Dispatcher.UIThread.UnhandledException += (_, e) => log.LogCritical(e.Exception, "Unhandled error on the UI thread");
        log.LogInformation("Library in {LibraryDir}", services.GetRequiredService<AppPaths>().LibraryDir);
        // Off the UI thread: blocking on an await that resumes on the dispatcher would deadlock
        Task.Run(async () =>
        {
            await services.GetRequiredService<Migrator>().MigrateAsync();
            // downloads cut off by closing the app would otherwise stay "downloading" forever
            await services.GetRequiredService<ITrackRepository>().ResetInterruptedDownloadsAsync();
        }).GetAwaiter().GetResult();

        return (services, services.GetRequiredService<MainViewModel>());
    }

    /// <summary>Loads what the first page shows, once the view is there.</summary>
    private static void Load(MainViewModel main)
    {
        main.Library.LoadCommand.Execute(null);
        // opens the audio device in the background, so the first click on a cover plays at once
        main.Player.WarmUp();
        // the "add to playlist" menus need the playlists before the page is opened
        main.Playlists.LoadCommand.Execute(null);
    }
}
