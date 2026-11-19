namespace Tapedeck.Core.Playlists;

/// <summary>
/// Keeps one <c>.m3u8</c> per playlist in the library folder, so copying the folder to the phone brings the
/// playlists along. Only files this installation wrote are ever deleted: the user's own playlists stay, and so do
/// Tapedeck's files from another installation, e.g. the computer's playlists copied next to the phone app's.
/// </summary>
public sealed class PlaylistExporter(IPlaylistRepository playlists, AppPaths paths)
{
    // the names of the files written by the last sync, one per line; a file of a renamed or deleted playlist is
    // deleted only if it is listed here
    private string WrittenListPath => Path.Combine(paths.DataDir, "exported-playlists.txt");

    /// <summary>Writes every playlist and deletes the files it wrote for playlists that were renamed or deleted.</summary>
    /// <returns>Number of files written or deleted.</returns>
    public async Task<int> SyncAsync()
    {
        Directory.CreateDirectory(paths.LibraryDir);
        var earlier = this.ReadWritten();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changes = 0;

        foreach (var playlist in await playlists.ListAsync())
        {
            var tracks = await playlists.GetTracksAsync(playlist.Id);
            var content = M3uWriter.Build(playlist.Name, tracks, file => File.Exists(paths.ToLibraryPath(file)));
            var fileName = M3uWriter.FileNameFor(playlist.Name);
            written.Add(fileName);

            var path = paths.ToLibraryPath(fileName);
            // unchanged files keep their date, so a phone sync tool does not copy them again
            if (File.Exists(path) && await File.ReadAllTextAsync(path, M3uWriter.ENCODING) == content)
                continue;
            await File.WriteAllTextAsync(path, content, M3uWriter.ENCODING);
            changes++;
        }

        foreach (var fileName in earlier.Where(f => !written.Contains(f)))
        {
            if (DeleteOwn(Path.Combine(paths.LibraryDir, fileName)))
                changes++;
        }

        this.SaveWritten(written);
        return changes;
    }

    /// <summary>Deletes the playlist files this installation wrote into a folder, e.g. the old library after a move.</summary>
    public void RemoveExported(string folder)
    {
        foreach (var fileName in this.ReadWritten())
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

    private HashSet<string> ReadWritten()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(this.WrittenListPath))
            return names;
        foreach (var line in File.ReadLines(this.WrittenListPath))
        {
            // only plain file names: the list must never reach outside the folder
            var name = line.Trim();
            if (name.Length > 0 && name == Path.GetFileName(name) && name.EndsWith(M3uWriter.EXTENSION, StringComparison.OrdinalIgnoreCase))
                names.Add(name);
        }
        return names;
    }

    private void SaveWritten(IEnumerable<string> names)
    {
        Directory.CreateDirectory(paths.DataDir);
        File.WriteAllLines(this.WrittenListPath, names.Order(StringComparer.OrdinalIgnoreCase));
    }
}
