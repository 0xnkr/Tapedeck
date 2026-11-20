namespace Tapedeck.Core.Settings;

/// <summary>
/// Where the music ends up. The library is always Opus; for Apple, Tapedeck also keeps <c>.m4a</c> copies
/// in a folder of their own (<see cref="Apple.AppleExporter"/>), because Apple Music cannot play Opus.
/// </summary>
public enum TargetSystem
{
    /// <summary>Android phones: the Opus library as it is.</summary>
    Google,

    /// <summary>Apple Music on a Mac, iPhone or iTunes: AAC copies next to the library.</summary>
    Apple,
}

/// <summary>The codes in <c>settings.json</c>; <c>null</c> follows the system Tapedeck runs on.</summary>
public static class TargetSystemCode
{
    private const string GOOGLE = "google";
    private const string APPLE = "apple";

    /// <summary>A Mac targets Apple, everything else Google.</summary>
    public static TargetSystem Default => OperatingSystem.IsMacOS() ? TargetSystem.Apple : TargetSystem.Google;

    public static TargetSystem Parse(string? code)
    {
        // a phone only plays its own Opus library
        if (OperatingSystem.IsAndroid())
            return TargetSystem.Google;
        return code switch
        {
            GOOGLE => TargetSystem.Google,
            APPLE => TargetSystem.Apple,
            _ => Default,
        };
    }

    public static string Of(TargetSystem system) => system switch
    {
        TargetSystem.Apple => APPLE,
        _ => GOOGLE,
    };
}
