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
/// <param name="LastFmApiKey">The user's own Last.fm API key for genres of tracks MusicBrainz has none for; <c>null</c> turns Last.fm off.</param>
/// <param name="ParallelDownloads">
/// How many tracks "Download pending" and similar download at once, 1 to <see cref="MAX_PARALLEL_DOWNLOADS"/>;
/// <c>null</c> means <see cref="DEFAULT_PARALLEL_DOWNLOADS"/>.
/// </param>
/// <param name="TargetPlayer">The phone player the tags are written for, as <see cref="TargetPlayerCode"/>; <c>null</c> follows the Opus standard.</param>
/// <param name="LyricsAuto">Ask LRCLIB in the background for the lyrics of downloaded tracks; <c>null</c> means on.</param>
/// <param name="TargetSystem">Google or Apple as <see cref="TargetSystemCode"/>; <c>null</c> follows the system Tapedeck runs on.</param>
/// <param name="AppleExportDir">Absolute folder of the <c>.m4a</c> copies; <c>null</c> uses <c>Music/Tapedeck (Apple)</c>.</param>
public sealed record AppSettings(
    string? Language = null,
    string? Accent = null,
    string? LibraryDir = null,
    bool? MusicBrainzAuto = null,
    bool? KeepDeletesFiles = null,
    bool? LevelPlayback = null,
    string? LastFmApiKey = null,
    int? ParallelDownloads = null,
    string? TargetPlayer = null,
    bool? LyricsAuto = null,
    string? TargetSystem = null,
    string? AppleExportDir = null)
{
    /// <summary>
    /// Clearly faster than one at a time, and still below what makes YouTube refuse. A phone takes two: each download
    /// also starts Python and ffmpeg.
    /// </summary>
    public static readonly int DEFAULT_PARALLEL_DOWNLOADS = OperatingSystem.IsAndroid() ? 2 : 3;

    public const int MAX_PARALLEL_DOWNLOADS = 4;
}
