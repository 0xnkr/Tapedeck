using Avalonia;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tapedeck.Core;
using Tapedeck.Core.Settings;
using Tapedeck.Services;

namespace Tapedeck;

abstract class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // first of all, so even a failing start ends up in the log
        var log = FileLog.Start(AppPaths.DefaultDataDir())?.CreateLogger("App");
        if (log is not null)
            LogStart(log);

        // Before any window or resource lookup, so every text uses the chosen language
        var settings = new SettingsStore(AppPaths.DefaultDataDir()).Load();
        StartupOptions.Parse(args).ApplyLanguage(settings.Language);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>The first lines of a run: what runs where, and every error nobody caught.</summary>
    private static void LogStart(ILogger log)
    {
        log.LogInformation("Tapedeck {Version} started on {Os} ({Architecture}), .NET {Runtime}",
            AppInfo.DISPLAY, RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture,
            Environment.Version);
        log.LogInformation("App data in {DataDir}", AppPaths.DefaultDataDir());

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.LogCritical(e.ExceptionObject as Exception, "Unhandled error, Tapedeck closes");
        // a fire-and-forget task that failed: nothing waits for it, so its error would be lost
        TaskScheduler.UnobservedTaskException += (_, e) =>
            log.LogError(e.Exception, "Unobserved error in a background task");
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}