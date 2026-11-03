using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Tapedeck.Core;
using Tapedeck.Data;
using Tapedeck.Theme;
using Tapedeck.ViewModels;
using Tapedeck.Views;

namespace Tapedeck;

public partial class App : Application
{
    private AccentService _accent = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Before any window exists, so the accent resources are there for the first layout and the previewer
        _accent = new AccentService(Resources);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (StartupOptions.Parse(desktop.Args ?? []).Accent is { } accent)
                _accent.Apply(accent);

            var services = AppServices.Build(_accent);
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
        }

        base.OnFrameworkInitializationCompleted();
    }
}
