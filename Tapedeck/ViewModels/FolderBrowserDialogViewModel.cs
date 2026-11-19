using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Resources;

namespace Tapedeck.ViewModels;

/// <summary>Choose a folder below <paramref name="Root"/>, starting in <paramref name="StartFolder"/>.</summary>
/// <param name="Root">The highest folder the user may go up to, e.g. the phone's storage.</param>
/// <param name="RootName">How <paramref name="Root"/> is shown, e.g. "Phone storage".</param>
public sealed record FolderBrowserRequest(string Title, string Root, string RootName, string? StartFolder);

/// <summary>A folder in the list; opening it shows its subfolders.</summary>
public sealed record FolderEntry(string Name, string Path);

/// <summary>
/// Tapedeck's own folder picker, for where the system picker is no use: Android's opens at the storage root, where it
/// shows nothing and allows nothing. With the access to all files the app can list the folders itself.
/// </summary>
public partial class FolderBrowserDialogViewModel : ViewModelBase, IDialog
{
    private readonly TaskCompletionSource<string?> answer = new();
    private readonly string root;

    public FolderBrowserDialogViewModel(FolderBrowserRequest request)
    {
        this.Request = request;
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Root));
        var start = request.StartFolder is null ? this.root : Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.StartFolder));
        if (!this.IsBelowRoot(start))
            start = this.root;
        // a folder that is gone starts at its nearest existing parent
        while (start != this.root && !Directory.Exists(start))
            start = Path.GetDirectoryName(start) ?? this.root;
        this.Show(start);
    }

    public FolderBrowserRequest Request { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Location))]
    [NotifyPropertyChangedFor(nameof(CanGoUp))]
    [NotifyCanExecuteChangedFor(nameof(UpCommand))]
    public partial string CurrentPath { get; private set; } = string.Empty;

    /// <summary>The open folder below the root, e.g. "Phone storage / Music / Tapedeck".</summary>
    public string Location => this.CurrentPath == this.root
        ? this.Request.RootName
        : $"{this.Request.RootName} / {Path.GetRelativePath(this.root, this.CurrentPath).Replace(Path.DirectorySeparatorChar, '/').Replace("/", " / ")}";

    public bool CanGoUp => this.CurrentPath != this.root;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<FolderEntry> Folders { get; private set; } = [];

    public bool IsEmpty => this.Folders.Count == 0;

    /// <summary>The field for a new folder's name is open.</summary>
    [ObservableProperty]
    public partial bool IsNaming { get; private set; }

    [ObservableProperty]
    public partial string NewName { get; set; } = string.Empty;

    /// <summary>Why the last step did not work; <c>null</c> while all is fine.</summary>
    [ObservableProperty]
    public partial string? Error { get; private set; }

    /// <summary>Completes with the chosen folder, or <c>null</c> when cancelled.</summary>
    public Task<string?> Answer => this.answer.Task;

    partial void OnNewNameChanged(string value) => this.Error = null;

    [RelayCommand]
    private void Open(FolderEntry folder) => this.Show(folder.Path);

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void Up()
    {
        if (Path.GetDirectoryName(this.CurrentPath) is { } parent && this.IsBelowRoot(parent))
            this.Show(parent);
    }

    [RelayCommand]
    private void StartNaming()
    {
        this.NewName = string.Empty;
        this.Error = null;
        this.IsNaming = true;
    }

    [RelayCommand]
    private void StopNaming()
    {
        this.IsNaming = false;
        this.Error = null;
    }

    /// <summary>Creates the folder and opens it, so it can be chosen right away.</summary>
    [RelayCommand]
    private void CreateFolder()
    {
        var name = this.NewName.Trim();
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.IndexOfAny(['/', '\\']) >= 0)
        {
            this.Error = Strings.FolderBrowser_InvalidName;
            return;
        }

        var path = Path.Combine(this.CurrentPath, name);
        if (Directory.Exists(path) || File.Exists(path))
        {
            this.Error = Strings.FolderBrowser_Exists;
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Error = string.Format(Strings.FolderBrowser_CreateFailed, ex.Message);
            return;
        }
        this.IsNaming = false;
        this.Show(path);
    }

    [RelayCommand]
    private void Choose() => this.answer.TrySetResult(this.CurrentPath);

    [RelayCommand]
    private void Cancel() => this.answer.TrySetResult(null);

    void IDialog.Cancel()
    {
        // Escape or the back gesture first closes the name field, then the dialog
        if (this.IsNaming)
            this.StopNaming();
        else
            this.Cancel();
    }

    private bool IsBelowRoot(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(path);
        return full == this.root || full.StartsWith(this.root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private void Show(string path)
    {
        this.Error = null;
        this.CurrentPath = Path.TrimEndingDirectorySeparator(path);
        try
        {
            this.Folders = new DirectoryInfo(this.CurrentPath).EnumerateDirectories()
                // hidden folders and Android's app folder are no place for a music library
                .Where(d => !d.Name.StartsWith('.') && !(this.CurrentPath == this.root && d.Name == "Android"))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(d => new FolderEntry(d.Name, d.FullName))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Folders = [];
            this.Error = string.Format(Strings.FolderBrowser_ReadFailed, ex.Message);
        }
    }
}
