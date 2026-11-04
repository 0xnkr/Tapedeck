using System.Text.Json;

namespace Tapedeck.Core.Settings;

/// <summary>
/// Reads and writes <c>settings.json</c> in the app data folder. A JSON file instead of the database,
/// because the language is needed at startup before the database is migrated.
/// </summary>
public sealed class SettingsStore(string dataDir)
{
    private static readonly JsonSerializerOptions JSON_OPTIONS = new() { WriteIndented = true };

    public string FilePath => Path.Combine(dataDir, "settings.json");

    /// <summary>Current settings; a missing or broken file gives the defaults.</summary>
    public AppSettings Load()
    {
        try
        {
            return File.Exists(this.FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(this.FilePath), JSON_OPTIONS) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(dataDir);
        // write to a temp file first, so a crash never leaves a half written settings file
        var temp = this.FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JSON_OPTIONS));
        File.Move(temp, this.FilePath, overwrite: true);
    }

    /// <summary>Loads, changes and saves in one step.</summary>
    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        var settings = change(this.Load());
        this.Save(settings);
        return settings;
    }
}
