namespace Tapedeck.Core;

/// <summary>Storage locations of the app. Cross-platform via <see cref="Environment.SpecialFolder"/>.</summary>
public sealed class AppPaths(string dataDir, string libraryDir)
{
    /// <summary>App data: database, tools, thumbnails.</summary>
    public string DataDir { get; } = dataDir;

    /// <summary>Flat music library. <c>tracks.file_path</c> is relative to this folder.</summary>
    public string LibraryDir { get; } = libraryDir;

    public string DatabasePath => Path.Combine(DataDir, "tapedeck.db");
    public string ToolsDir => Path.Combine(DataDir, "tools");
    public string ThumbsDir => Path.Combine(DataDir, "thumbs");
    public string TempDir => Path.Combine(DataDir, "tmp");

    public static AppPaths Default()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrEmpty(music))
            music = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");
        return new AppPaths(Path.Combine(appData, "Tapedeck"), Path.Combine(music, "Tapedeck"));
    }

    public void EnsureDirectories()
    {
        foreach (var dir in new[] { DataDir, LibraryDir, ToolsDir, ThumbsDir, TempDir })
            Directory.CreateDirectory(dir);
    }

    public string ToLibraryPath(string relativePath) => Path.Combine(LibraryDir, relativePath);
}
