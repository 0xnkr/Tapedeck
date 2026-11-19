using Android.Content;
using Android.Provider;
using AndroidX.Core.Content;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core;
using Tapedeck.Resources;
using Tapedeck.Services;
using Tapedeck.ViewModels;
using AndroidApplication = Android.App.Application;
using AndroidEnvironment = Android.OS.Environment;
using AndroidUri = Android.Net.Uri;
using JavaFile = Java.IO.File;

namespace Tapedeck.Android.Services;

/// <summary>
/// The phone's file dialogs answer with <c>content://</c> links instead of paths. With the access to all files a file
/// or folder in the phone's own storage is a path again; a file from elsewhere (a cloud drive) is copied into the temp
/// folder to be read. Folders are chosen in Tapedeck's own browser. Showing a folder opens the Files app, opening a
/// file (the log) shares it.
/// </summary>
public sealed class AndroidPlatformService(AppPaths paths, DialogService dialogs, ILogger<AndroidPlatformService>? logger = null)
    : PlatformService
{
    private const string EXTERNAL_STORAGE = "com.android.externalstorage.documents";
    private const string DOWNLOADS = "com.android.providers.downloads.documents";

    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    public override Task OpenFolderAsync(string path)
    {
        var folder = DocumentLink(path) ?? DocumentLink(paths.LibraryDir)
                     ?? DocumentsContract.BuildDocumentUri(EXTERNAL_STORAGE, "primary:");
        var intent = new Intent(Intent.ActionView)
            .SetDataAndType(folder, DocumentsContract.Document.MimeTypeDir)
            .AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
        this.Start(intent, "folder");
        return Task.CompletedTask;
    }

    public override Task OpenFileAsync(string path)
    {
        var context = AndroidApplication.Context;
        // declared in AndroidManifest.xml after the package name, shares files of the app's own storage
        var uri = FileProvider.GetUriForFile(context, $"{context.PackageName}.files", new JavaFile(path));
        var send = new Intent(Intent.ActionSend)
            .SetType("text/plain")
            .PutExtra(Intent.ExtraStream, uri)
            .AddFlags(ActivityFlags.GrantReadUriPermission);
        this.Start(Intent.CreateChooser(send, (string?)null)!.AddFlags(ActivityFlags.NewTask), "share");
        return Task.CompletedTask;
    }

    protected override string? ToLocalPath(IStorageItem item) => item.TryGetLocalPath() ?? Resolve(item.Path);

    /// <summary>
    /// Tapedeck's own folder browser instead of the system picker: that one opens at the storage's root, where it shows
    /// nothing and lets the user choose nothing. With the access to all files the app lists the folders itself.
    /// </summary>
    public override async Task<string?> PickFolderAsync(string title, string? startFolder)
    {
        // the browser works on the real path (/storage/emulated/0/…); the library keeps the form it was set in
        // (/sdcard/…), else one folder would look like two when moving
        var shown = Environment.GetEnvironmentVariable("EXTERNAL_STORAGE") ?? AndroidEnvironment.ExternalStorageDirectory!.AbsolutePath;
        var root = new JavaFile(shown).CanonicalPath;
        var start = startFolder is null ? null : new JavaFile(startFolder).CanonicalPath;
        var chosen = await dialogs.PickFolderAsync(new FolderBrowserRequest(title, root, Strings.FolderBrowser_Storage, start));
        if (chosen is null)
            return null;
        var relative = Path.GetRelativePath(root, chosen);
        return relative == "." ? shown : Path.Join(shown, relative);
    }

    /// <summary>The external storage's document link of a folder in the phone's own storage; <c>null</c> elsewhere.</summary>
    private static AndroidUri? DocumentLink(string path)
    {
        var root = AndroidEnvironment.ExternalStorageDirectory!.AbsolutePath;
        var full = new JavaFile(path).CanonicalPath;
        var canonicalRoot = new JavaFile(root).CanonicalPath;
        if (!full.StartsWith(canonicalRoot, StringComparison.Ordinal))
            return null;
        var relative = Path.GetRelativePath(canonicalRoot, full);
        return DocumentsContract.BuildDocumentUri(EXTERNAL_STORAGE, relative == "." ? "primary:" : $"primary:{relative}");
    }

    protected override async Task<string?> ToLocalPathAsync(IStorageFile file)
    {
        if (this.ToLocalPath(file) is { } path && File.Exists(path))
            return path;

        // not in the phone's storage: read a copy
        var folder = Path.Combine(paths.TempDir, "picked");
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, Path.GetFileName(file.Name));
        await using (var source = await file.OpenReadAsync())
        await using (var target = File.Create(copy))
            await source.CopyToAsync(target);
        this.log.LogInformation("Copied the picked file {Name} to read it", file.Name);
        return copy;
    }

    /// <summary>
    /// The path behind a link of the phone's own storage: documents and folder trees of the external storage
    /// (<c>primary:Music/Tapedeck</c>) and downloads that name their file (<c>raw:/storage/…</c>).
    /// </summary>
    private static string? Resolve(Uri link)
    {
        if (link.IsFile)
            return link.LocalPath;
        if (link.Scheme != ContentResolver.SchemeContent)
            return null;

        var uri = AndroidUri.Parse(link.OriginalString)!;
        var context = AndroidApplication.Context;
        var documentId = DocumentsContract.IsDocumentUri(context, uri) ? DocumentsContract.GetDocumentId(uri)
            : DocumentsContract.IsTreeUri(uri) ? DocumentsContract.GetTreeDocumentId(uri)
            : null;
        if (documentId is null)
            return null;

        switch (uri.Authority)
        {
            case EXTERNAL_STORAGE:
            {
                var parts = documentId.Split(':', 2);
                var root = parts[0].Equals("primary", StringComparison.OrdinalIgnoreCase)
                    ? AndroidEnvironment.ExternalStorageDirectory!.AbsolutePath
                    // a memory card: /storage/1234-5678
                    : Path.Combine("/storage", parts[0]);
                return parts.Length > 1 && parts[1].Length > 0 ? Path.Combine(root, parts[1]) : root;
            }
            case DOWNLOADS when documentId.StartsWith("raw:", StringComparison.Ordinal):
                return documentId[4..];
            default:
                return null;
        }
    }

    private void Start(Intent intent, string what)
    {
        try
        {
            AndroidApplication.Context.StartActivity(intent);
        }
        catch (ActivityNotFoundException ex)
        {
            this.log.LogWarning("No app to open the {What}: {Error}", what, ex.Message);
        }
    }
}
