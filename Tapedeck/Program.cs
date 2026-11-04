using Avalonia;
using System;
using Tapedeck.Core;
using Tapedeck.Core.Settings;

namespace Tapedeck;

abstract class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Before any window or resource lookup, so every text uses the chosen language
        var settings = new SettingsStore(AppPaths.DefaultDataDir()).Load();
        StartupOptions.Parse(args).ApplyLanguage(settings.Language);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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