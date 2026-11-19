using System.Globalization;
using System.Runtime.InteropServices;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tapedeck.Android.Services;
using Tapedeck.Core;
using Tapedeck.Core.Services;
using Tapedeck.Core.Settings;
using Tapedeck.Services;

namespace Tapedeck.Android;

/// <summary>
/// The process: starts the log and the language before anything else, then the shared app with the phone's
/// own services (the tools inside the APK, AudioTrack for playback).
/// </summary>
[Application]
public sealed class MainApplication(nint javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<App>(javaReference, transfer)
{
    public override void OnCreate()
    {
        // first of all, so even a failing start ends up in the log
        var log = FileLog.Start(AppPaths.DefaultDataDir())?.CreateLogger("App");
        if (log is not null)
        {
            log.LogInformation("Tapedeck {Version} started on {Os} ({Architecture}), .NET {Runtime}",
                AppInfo.DISPLAY, RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture, Environment.Version);
            log.LogInformation("App data in {DataDir}", AppPaths.DefaultDataDir());
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                log.LogCritical(e.ExceptionObject as Exception, "Unhandled error, Tapedeck closes");
            TaskScheduler.UnobservedTaskException += (_, e) =>
                log.LogError(e.Exception, "Unobserved error in a background task");
        }

        // Before any view or resource lookup, so every text uses the chosen language; else the phone's language
        if (new SettingsStore(AppPaths.DefaultDataDir()).Load().Language is { } language)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(language);
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }
            catch (CultureNotFoundException)
            {
                // a broken setting keeps the phone's language
            }
        }

        base.OnCreate();
        // keeps the app running while it works, also with the screen off, and the media index up to date
        (Avalonia.Application.Current as App)?.Services?.GetService<WorkNotifier>();
    }

    protected override AppBuilder CreateAppBuilder() =>
        AppBuilder.Configure(() => new App { ConfigurePlatform = ConfigurePlatform }).UseAndroid();

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();

    /// <summary>
    /// What the phone does its own way: the tools inside the APK, AudioTrack for playback, file dialogs with content
    /// links, the foreground service while working.
    /// </summary>
    private static void ConfigurePlatform(IServiceCollection services)
    {
        services.AddSingleton<IToolHost, AndroidToolHost>();
        services.AddSingleton<PlayerService, AndroidPlayerService>();
        // registered after the shared one, so it wins
        services.AddSingleton<IPlatformService, AndroidPlatformService>();
        services.AddSingleton<WorkNotifier>();
        services.AddSingleton<MediaIndex>();
    }
}
