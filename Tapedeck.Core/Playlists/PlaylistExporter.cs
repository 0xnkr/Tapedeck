using Tapedeck.Core.Models;

namespace Tapedeck.Core.Playlists;

/// <summary>Where the playlist files go and which file each track's entry names.</summary>
/// <param name="Folder">Folder of the <c>.m3u8</c> files, which the entries are relative to.</param>
/// <param name="WrittenListName">File in the app data folder that lists the playlist files written there.</param>
/// <param name="FileFor">The entry of a track, <c>null</c> to leave it out.</param>
public sealed record PlaylistTarget(string Folder, string WrittenListName, Func<TrackDisplay, string?> FileFor);

/// <summary>
/// Keeps one <c>.m3u8</c> per playlist in the library folder, so copying the folder to the phone brings the
/// playlists along. Only files this installation wrote are ever deleted: the user's own playlists stay, and so do
/// Tapedeck's files from another installation, e.g. the computer's playlists copied next to the phone app's.
/// The Apple export writes its playlists the same way into its own folder (<see cref="SyncAsync(PlaylistTarget)"/>).
/// </summary>
public sealed class PlaylistExporter(IPlaylistRepository playlists, AppPaths paths)
{
    // the names of the files written by the last sync, one per line; a file of a renamed or deleted playlist is
    // deleted only if it is listed here
    private const string LIBRARY_LIST = "exported-playlists.txt";

    /// <summary>The playlists of the Opus library, next to the files in the library folder.</summary>
    private PlaylistTarget Library => new(paths.LibraryDir, LIBRARY_LIST,
        track => track.FilePath is { } file && File.Exists(paths.ToLibraryPath(file)) ? file : null);

    /// <summary>Writes every playlist and deletes the files it wrote for playlists that were renamed or deleted.</summary>
    /// <returns>Number of files written or deleted.</returns>
    public Task<int> SyncAsync() => this.SyncAsync(this.Library);

    /// <inheritdoc cref="SyncAsync()"/>
    public async Task<int> SyncAsync(PlaylistTarget target)
    {
        Directory.CreateDirectory(target.Folder);
        var earlier = this.ReadWritten(target.WrittenListName);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changes = 0;

        foreach (var playlist in await playlists.ListAsync())
        {
            var tracks = await playlists.GetTracksAsync(playlist.Id);
            var content = M3uWriter.Build(playlist.Name, tracks, target.FileFor);
            var fileName = M3uWriter.FileNameFor(playlist.Name);
            written.Add(fileName);

            var path = Path.Combine(target.Folder, fileName);
            // unchanged files keep their date, so a phone sync tool does not copy them again
            if (File.Exists(path) && await File.ReadAllTextAsync(path, M3uWriter.ENCODING) == content)
                continue;
            await File.WriteAllTextAsync(path, content, M3uWriter.ENCODING);
            changes++;
        }

        foreach (var fileName in earlier.Where(f => !written.Contains(f)))
        {
            if (DeleteOwn(Path.Combine(target.Folder, fileName)))
                changes++;
        }

        this.SaveWritten(target.WrittenListName, written);
        return changes;
    }

    /// <summary>Deletes the playlist files this installation wrote into a folder, e.g. the old library after a move.</summary>
    public void RemoveExported(string folder) => this.RemoveExported(folder, LIBRARY_LIST);

    /// <summary>Deletes the playlist files listed in <paramref name="writtenListName"/> from a folder.</summary>
    public void RemoveExported(string folder, string writtenListName)
    {
        foreach (var fileName in this.ReadWritten(writtenListName))
            DeleteOwn(Path.Combine(folder, fileName));
    }

    /// <summary>Deletes the file if it still is one of Tapedeck's; the user may have replaced it with their own.</summary>
    private static bool DeleteOwn(string path)
    {
        if (!File.Exists(path) || !M3uWriter.IsOwnFile(path))
            return false;
        File.Delete(path);
        return true;
    }

    private HashSet<string> ReadWritten(string listName)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listPath = Path.Combine(paths.DataDir, listName);
        if (!File.Exists(listPath))
            return names;
        foreach (var line in File.ReadLines(listPath))
        {
            // only plain file names: the list must never reach outside the folder
            var name = line.Trim();
            if (name.Length > 0 && name == Path.GetFileName(name) && name.EndsWith(M3uWriter.EXTENSION, StringComparison.OrdinalIgnoreCase))
                names.Add(name);
        }
        return names;
    }

    private void SaveWritten(string listName, IEnumerable<string> names)
    {
        Directory.CreateDirectory(paths.DataDir);
        File.WriteAllLines(Path.Combine(paths.DataDir, listName), names.Order(StringComparer.OrdinalIgnoreCase));
    }
}
