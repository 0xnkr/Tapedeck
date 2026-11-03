using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace Tapedeck.Services;

/// <summary>Desktop functions that need the window (clipboard, opening links), kept out of the view models.</summary>
public interface IPlatformService
{
    Task<string?> GetClipboardTextAsync();

    Task OpenUrlAsync(string url);

    Task OpenFolderAsync(string path);
}

public sealed class PlatformService : IPlatformService
{
    private static TopLevel? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<string?> GetClipboardTextAsync() =>
        MainWindow?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

    public async Task OpenUrlAsync(string url)
    {
        if (MainWindow is { } window)
            await window.Launcher.LaunchUriAsync(new Uri(url));
    }

    public async Task OpenFolderAsync(string path)
    {
        if (MainWindow is { } window)
            await window.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }
}
