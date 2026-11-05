namespace Tapedeck.Core;

/// <summary>Storage locations of the app. Cross-platform via <see cref="Environment.SpecialFolder"/>.</summary>
public sealed class AppPaths(string dataDir, string libraryDir)
{
    /// <summary>App data: database, settings, tools, thumbnails.</summary>
    public string DataDir { get; } = dataDir;

    /// <summary>
    /// Flat music library. <c>tracks.file_path</c> is relative to this folder.
    /// Services read it on every use, so <see cref="ChangeLibraryDir"/> takes effect right away.
    /// </summary>
    public string LibraryDir { get; private set; } = libraryDir;

    public string DatabasePath => Path.Combine(this.DataDir, "tapedeck.db");
    public string ToolsDir => Path.Combine(this.DataDir, "tools");
    public string ThumbsDir => Path.Combine(this.DataDir, "thumbs");
    /// <summary>Square covers from the Cover Art Archive, one per release.</summary>
    public string CoversDir => Path.Combine(this.DataDir, "covers");
    public string TempDir => Path.Combine(this.DataDir, "tmp");

    /// <summary>The app data folder, which also holds the settings and therefore cannot be configured.</summary>
    public static string DefaultDataDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tapedeck");

    public static string DefaultLibraryDir()
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrEmpty(music))
            music = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");
        return Path.Combine(music, "Tapedeck");
    }

    /// <param name="libraryDir">The configured library folder, or <c>null</c> for the default.</param>
    public static AppPaths Default(string? libraryDir = null) =>
        new(DefaultDataDir(), string.IsNullOrWhiteSpace(libraryDir) ? DefaultLibraryDir() : libraryDir);

    public void EnsureDirectories()
    {
        foreach (var dir in new[] { this.DataDir, this.LibraryDir, this.ToolsDir, this.ThumbsDir, this.CoversDir, this.TempDir })
            Directory.CreateDirectory(dir);
    }

    /// <summary>Points the library to another folder; moving the files is up to the caller (<c>LibraryMover</c>).</summary>
    public void ChangeLibraryDir(string libraryDir) => this.LibraryDir = libraryDir;

    public string ToLibraryPath(string relativePath) => Path.Combine(this.LibraryDir, relativePath);
}
