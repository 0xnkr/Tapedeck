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
public sealed record AppSettings(
    string? Language = null,
    string? Accent = null,
    string? LibraryDir = null,
    bool? MusicBrainzAuto = null,
    bool? KeepDeletesFiles = null,
    bool? LevelPlayback = null,
    string? LastFmApiKey = null);
