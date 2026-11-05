using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace Tapedeck.Services;

/// <summary>A file type for open and save dialogs, e.g. <c>new FileFilter("CSV", "csv")</c>.</summary>
public sealed record FileFilter(string Name, string Extension);

/// <summary>Desktop functions that need the window (clipboard, links, folders, file dialogs), kept out of the view models.</summary>
public interface IPlatformService
{
    Task<string?> GetClipboardTextAsync();

    Task SetClipboardTextAsync(string text);

    Task OpenUrlAsync(string url);

    Task OpenFolderAsync(string path);

    /// <returns>The chosen folder as local path, or <c>null</c> when cancelled.</returns>
    Task<string?> PickFolderAsync(string title, string? startFolder);

    /// <returns>The chosen file as local path, or <c>null</c> when cancelled.</returns>
    Task<string?> PickOpenFileAsync(string title, FileFilter filter);

    /// <returns>The target file as local path, or <c>null</c> when cancelled.</returns>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, FileFilter filter);
}

public sealed class PlatformService : IPlatformService
{
    private static TopLevel? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<string?> GetClipboardTextAsync() =>
        MainWindow?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

    public async Task SetClipboardTextAsync(string text)
    {
        if (MainWindow?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

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

    public async Task<string?> PickFolderAsync(string title, string? startFolder)
    {
        if (MainWindow?.StorageProvider is not { } storage)
            return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = startFolder is null ? null : await storage.TryGetFolderFromPathAsync(startFolder),
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickOpenFileAsync(string title, FileFilter filter)
    {
        if (MainWindow?.StorageProvider is not { } storage)
            return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [ToFileType(filter)],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, FileFilter filter)
    {
        if (MainWindow?.StorageProvider is not { } storage)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = filter.Extension,
            FileTypeChoices = [ToFileType(filter)],
        });
        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType ToFileType(FileFilter filter) =>
        new(filter.Name) { Patterns = [$"*.{filter.Extension}"] };
}
