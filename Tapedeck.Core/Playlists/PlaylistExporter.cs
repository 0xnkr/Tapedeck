namespace Tapedeck.Core.Playlists;

/// <summary>
/// Keeps one <c>.m3u8</c> per playlist in the library folder, so copying the folder to the phone brings the
/// playlists along. Files without Tapedeck's marker (the user's own playlists) are never touched.
/// </summary>
public sealed class PlaylistExporter(IPlaylistRepository playlists, AppPaths paths)
{
    /// <summary>Writes every playlist and deletes Tapedeck's files of playlists that were renamed or deleted.</summary>
    /// <returns>Number of files written or deleted.</returns>
    public async Task<int> SyncAsync()
    {
        Directory.CreateDirectory(paths.LibraryDir);
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

        foreach (var path in Directory.EnumerateFiles(paths.LibraryDir, "*" + M3uWriter.EXTENSION))
        {
            if (written.Contains(Path.GetFileName(path)) || !M3uWriter.IsOwnFile(path))
                continue;
            File.Delete(path);
            changes++;
        }

        return changes;
    }

    /// <summary>Deletes Tapedeck's playlist files from a folder, e.g. the old library after a move.</summary>
    public static void RemoveExported(string folder)
    {
        if (!Directory.Exists(folder))
            return;
        foreach (var path in Directory.EnumerateFiles(folder, "*" + M3uWriter.EXTENSION))
        {
            if (M3uWriter.IsOwnFile(path))
                File.Delete(path);
        }
    }
}
