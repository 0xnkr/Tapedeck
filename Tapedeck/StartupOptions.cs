using System;
using System.Globalization;
using Avalonia.Media;

namespace Tapedeck;

/// <summary>
/// Command line options for trying things out without code changes:
/// <c>--lang en</c> overrides the UI language, <c>--accent #C8B6FF</c> the accent color.
/// They win over the saved settings for this run only.
/// </summary>
public sealed record StartupOptions(CultureInfo? Language, Color? Accent)
{
    public static StartupOptions Parse(string[] args)
    {
        CultureInfo? language = null;
        Color? accent = null;

        for (var i = 0; i + 1 < args.Length; i++)
        {
            switch (args[i])
            {
                case "--lang":
                    language = TryGetCulture(args[++i]);
                    break;
                case "--accent" when Color.TryParse(args[i + 1], out var color):
                    accent = color;
                    i++;
                    break;
            }
        }

        return new StartupOptions(language, accent);
    }

    /// <summary>
    /// Sets the UI culture for this run: <c>--lang</c>, else the saved language, else the system language.
    /// </summary>
    public void ApplyLanguage(string? savedLanguage)
    {
        var language = this.Language ?? (savedLanguage is null ? null : TryGetCulture(savedLanguage));
        if (language is null)
            return;
        CultureInfo.DefaultThreadCurrentUICulture = language;
        CultureInfo.CurrentUICulture = language;
    }

    private static CultureInfo? TryGetCulture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
