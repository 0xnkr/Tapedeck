using Avalonia;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tapedeck.Core;
using Tapedeck.Core.Services;
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

        var options = StartupOptions.Parse(args);
        if (options.SpotifyReport is { } spotifyHistory)
        {
            RunCommand(logging => SpotifyReportCommand.RunAsync(spotifyHistory, logging), log);
            return;
        }
        if (options.AudioReport is { } audioCount)
        {
            RunCommand(logging => AudioReportCommand.RunAsync(audioCount, logging), log);
            return;
        }
        if (options.SpotifyMatch is { } matchHistory)
        {
            RunCommand(logging => SpotifyMatchCommand.RunAsync(matchHistory, options.SpotifyMatchCount ?? SpotifyMatchCommand.DEFAULT_COUNT, logging), log);
            return;
        }

        // Before any window or resource lookup, so every text uses the chosen language
        var settings = new SettingsStore(AppPaths.DefaultDataDir()).Load();
        options.ApplyLanguage(settings.Language);
        BuildAvaloniaApp(options).StartWithClassicDesktopLifetime(args);
    }

    /// <summary>What the desktop does its own way: downloaded tools, OpenAL for playback.</summary>
    private static void ConfigurePlatform(IServiceCollection services)
    {
        services.AddSingleton<IToolHost, ToolService>();
        services.AddSingleton<PlayerService, OpenAlPlayerService>();
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

    /// <summary>Runs a command without a window; errors only reach the log, there is no console.</summary>
    private static void RunCommand(Func<ILoggerFactory, Task> command, ILogger? log)
    {
        using var logging = LoggerFactory.Create(builder =>
        {
            if (FileLog.Current is { } file)
                builder.AddProvider(file);
        });
        try
        {
            command(logging).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "Command failed");
            Environment.ExitCode = 1;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    private static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(StartupOptions.Parse([]));

    private static AppBuilder BuildAvaloniaApp(StartupOptions options)
        => AppBuilder.Configure(() => new App { StartupAccent = options.Accent, ConfigurePlatform = ConfigurePlatform })
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}