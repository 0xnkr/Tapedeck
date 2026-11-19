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
        // the phone's shared storage, where every music player finds the files (Music/Tapedeck);
        // .NET's MyMusic would be a folder inside the app that nobody else can read
        if (OperatingSystem.IsAndroid())
            return Path.Combine(Environment.GetEnvironmentVariable("EXTERNAL_STORAGE") ?? "/storage/emulated/0", "Music", "Tapedeck");

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
        foreach (var dir in new[] { this.DataDir, this.ToolsDir, this.ThumbsDir, this.CoversDir, this.TempDir })
            Directory.CreateDirectory(dir);
        this.TryCreateLibraryDir();
    }

    /// <summary>
    /// Creates the library folder if it can. On a phone it needs the access to all files, which the user may not
    /// have granted yet; downloads create it again when they need it.
    /// </summary>
    /// <returns>Whether the folder exists now.</returns>
    public bool TryCreateLibraryDir()
    {
        try
        {
            Directory.CreateDirectory(this.LibraryDir);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Points the library to another folder; moving the files is up to the caller (<c>LibraryMover</c>).</summary>
    public void ChangeLibraryDir(string libraryDir) => this.LibraryDir = libraryDir;

    public string ToLibraryPath(string relativePath) => Path.Combine(this.LibraryDir, relativePath);
}
