namespace Tapedeck.Core.Services;

/// <summary>Why the library cannot be moved to a folder.</summary>
public enum MoveProblem
{
    SameFolder,
    InsideCurrentFolder,
    NotWritable,
    NameTaken,
}

/// <summary>Result of <see cref="LibraryMover.CheckAsync"/>; <see cref="FileName"/> names the colliding file.</summary>
public sealed record MoveCheck(MoveProblem? Problem, string? FileName = null)
{
    public bool IsOk => this.Problem is null;
}

/// <summary>
/// Moves the downloaded files to another library folder. File paths in the database are relative
/// to the library, so only the files move and <see cref="AppPaths.LibraryDir"/> changes.
/// </summary>
public sealed class LibraryMover(ITrackRepository repository, AppPaths paths)
{
    /// <summary>Checks everything that could stop the move halfway, before any file is touched.</summary>
    public async Task<MoveCheck> CheckAsync(string newDir)
    {
        var from = Normalize(paths.LibraryDir);
        var to = Normalize(newDir);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(from, to, comparison))
            return new MoveCheck(MoveProblem.SameFolder);
        if (to.StartsWith(from + Path.DirectorySeparatorChar, comparison))
            return new MoveCheck(MoveProblem.InsideCurrentFolder);
        if (!CanWrite(to))
            return new MoveCheck(MoveProblem.NotWritable);

        foreach (var file in await this.ExistingFilesAsync())
        {
            if (File.Exists(Path.Combine(to, file)))
                return new MoveCheck(MoveProblem.NameTaken, file);
        }

        return new MoveCheck(null);
    }

    /// <summary>Moves all files that exist and switches <see cref="AppPaths.LibraryDir"/> to <paramref name="newDir"/>.</summary>
    /// <returns>Number of moved files.</returns>
    public async Task<int> MoveAsync(string newDir, IProgress<(int Done, int Total)>? progress = null)
    {
        var check = await this.CheckAsync(newDir);
        if (!check.IsOk)
            throw new InvalidOperationException($"The library cannot be moved to '{newDir}': {check.Problem} {check.FileName}".TrimEnd());

        var files = await this.ExistingFilesAsync();
        Directory.CreateDirectory(newDir);
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            // File.Move copies and deletes when the target is on another drive
            await Task.Run(() => File.Move(paths.ToLibraryPath(file), Path.Combine(newDir, file)));
            progress?.Report((i + 1, files.Count));
        }

        paths.ChangeLibraryDir(newDir);
        return files.Count;
    }

    /// <summary>Library-relative paths of downloaded files that are still there.</summary>
    private async Task<IReadOnlyList<string>> ExistingFilesAsync()
    {
        var tracks = await repository.ListDisplayAsync();
        return tracks
            .Select(t => t.FilePath)
            .OfType<string>()
            .Where(f => File.Exists(paths.ToLibraryPath(f)))
            .ToList();
    }

    private static string Normalize(string dir) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));

    private static bool CanWrite(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".tapedeck-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
