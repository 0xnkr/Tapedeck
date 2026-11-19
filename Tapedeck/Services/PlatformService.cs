using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace Tapedeck.Services;

/// <summary>A file type for open and save dialogs, e.g. <c>new FileFilter("CSV", "csv", "text/csv")</c>.</summary>
/// <param name="MimeType">What a phone's file picker filters by; the desktop goes by the extension.</param>
public sealed record FileFilter(string Name, string Extension, string MimeType);

/// <summary>Functions that need the window or the system (clipboard, links, folders, file dialogs), kept out of the view models.</summary>
public interface IPlatformService
{
    Task<string?> GetClipboardTextAsync();

    Task SetClipboardTextAsync(string text);

    Task OpenUrlAsync(string url);

    Task OpenFolderAsync(string path);

    /// <summary>Opens a file with the program the system uses for it, e.g. the log in a text editor.</summary>
    Task OpenFileAsync(string path);

    /// <returns>The chosen folder as local path, or <c>null</c> when cancelled.</returns>
    Task<string?> PickFolderAsync(string title, string? startFolder);

    /// <returns>The chosen file as local path, or <c>null</c> when cancelled.</returns>
    Task<string?> PickOpenFileAsync(string title, FileFilter filter);

    /// <returns>The target file as local path, or <c>null</c> when cancelled.</returns>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, FileFilter filter);
}

/// <summary>
/// Through Avalonia's top level: the window on the desktop, the view on a phone. A phone's head derives from it where
/// the system works differently (picked files are no paths there).
/// </summary>
public class PlatformService : IPlatformService
{
    private static TopLevel? MainWindow => App.CurrentTopLevel;

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

    public virtual async Task OpenFolderAsync(string path)
    {
        if (MainWindow is { } window)
            await window.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }

    public virtual async Task OpenFileAsync(string path)
    {
        if (MainWindow is { } window)
            await window.Launcher.LaunchFileInfoAsync(new FileInfo(path));
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
        return folders.Count > 0 ? this.ToLocalPath(folders[0]) : null;
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
        return files.Count > 0 ? await this.ToLocalPathAsync(files[0]) : null;
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
        return file is null ? null : this.ToLocalPath(file);
    }

    /// <summary>The folder or the file to write to as local path; <c>null</c> when it has none.</summary>
    protected virtual string? ToLocalPath(IStorageItem item) => item.TryGetLocalPath();

    /// <summary>A file to read as local path; <c>null</c> when it has none.</summary>
    protected virtual Task<string?> ToLocalPathAsync(IStorageFile file) => Task.FromResult(file.TryGetLocalPath());

    private static FilePickerFileType ToFileType(FileFilter filter) =>
        new(filter.Name) { Patterns = [$"*.{filter.Extension}"], MimeTypes = [filter.MimeType] };
}
