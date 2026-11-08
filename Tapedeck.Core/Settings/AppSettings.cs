namespace Tapedeck.Core.Settings;

/// <summary>User preferences. <c>null</c> means the default applies.</summary>
/// <param name="Language">UI culture name such as <c>en</c> or <c>de</c>; <c>null</c> follows the system.</param>
/// <param name="Accent">Accent color as hex (<c>#FFB870</c>); <c>null</c> uses the palette.</param>
/// <param name="LibraryDir">Absolute folder of the music library; <c>null</c> uses <c>Music/Tapedeck</c>.</param>
/// <param name="MusicBrainzAuto">Look up new tracks on MusicBrainz after an import; <c>null</c> means on.</param>
/// <param name="KeepDeletesFiles">
/// "Keep this one" for duplicates without asking: <c>true</c> also deletes the copies' files, <c>false</c> keeps them;
/// <c>null</c> asks every time.
/// </param>
/// <param name="LevelPlayback">The app's player plays all tracks equally loud; <c>null</c> means on. The track gain in the files does not depend on it.</param>
/// <param name="ParallelDownloads">
/// How many tracks "Download pending" and similar download at once, 1 to <see cref="MAX_PARALLEL_DOWNLOADS"/>;
/// <c>null</c> means <see cref="DEFAULT_PARALLEL_DOWNLOADS"/>.
/// </param>
public sealed record AppSettings(
    string? Language = null,
    string? Accent = null,
    string? LibraryDir = null,
    bool? MusicBrainzAuto = null,
    bool? KeepDeletesFiles = null,
    bool? LevelPlayback = null,
    int? ParallelDownloads = null)
{
    /// <summary>Clearly faster than one at a time, and still below what makes YouTube refuse.</summary>
    public const int DEFAULT_PARALLEL_DOWNLOADS = 3;

    public const int MAX_PARALLEL_DOWNLOADS = 4;
}
