namespace Tapedeck.Core.Settings;

/// <summary>User preferences. <c>null</c> means the default applies.</summary>
/// <param name="Language">UI culture name such as <c>en</c> or <c>de</c>; <c>null</c> follows the system.</param>
/// <param name="Accent">Accent color as hex (<c>#FFB870</c>); <c>null</c> uses the palette.</param>
/// <param name="LibraryDir">Absolute folder of the music library; <c>null</c> uses <c>Music/Tapedeck</c>.</param>
public sealed record AppSettings(string? Language = null, string? Accent = null, string? LibraryDir = null);
