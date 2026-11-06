using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Tapedeck.Core;
using Tapedeck.Core.Settings;
using Tapedeck.Data;
using Tapedeck.Theme;
using Tapedeck.ViewModels;
using Tapedeck.Views;

namespace Tapedeck;

public partial class App : Application
{
    private AccentService accent = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Before any window exists, so the accent resources are there for the first layout and the previewer
        this.accent = new AccentService(this.Resources);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new SettingsStore(AppPaths.DefaultDataDir());
            var settings = store.Load();
            // --accent for this run, else the saved accent, else the palette
            if (StartupOptions.Parse(desktop.Args ?? []).Accent is { } accent)
                this.accent.Apply(accent);
            else if (settings.Accent is { } savedAccent)
                this.accent.TryApply(savedAccent);

            var services = AppServices.Build(this.accent, store, settings);
            // Off the UI thread: blocking on an await that resumes on the dispatcher would deadlock
            Task.Run(async () =>
            {
                await services.GetRequiredService<Migrator>().MigrateAsync();
                // downloads cut off by closing the app would otherwise stay "downloading" forever
                await services.GetRequiredService<ITrackRepository>().ResetInterruptedDownloadsAsync();
            }).GetAwaiter().GetResult();

            var main = services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = main };
            desktop.Exit += (_, _) => services.Dispose();
            main.Library.LoadCommand.Execute(null);
            // opens the audio device in the background, so the first click on a cover plays at once
            main.Player.WarmUp();
            // the "add to playlist" menus need the playlists before the page is opened
            main.Playlists.LoadCommand.Execute(null);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
