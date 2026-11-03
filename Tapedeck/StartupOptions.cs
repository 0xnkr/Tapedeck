using System;
using System.Globalization;
using Avalonia.Media;

namespace Tapedeck;

/// <summary>
/// Command line options for trying things out without code changes:
/// <c>--lang en</c> overrides the UI language, <c>--accent #C8B6FF</c> the accent color.
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

    /// <summary>Sets the UI culture for this run; the system language applies when none is given.</summary>
    public void ApplyLanguage()
    {
        if (Language is null)
            return;
        CultureInfo.DefaultThreadCurrentUICulture = Language;
        CultureInfo.CurrentUICulture = Language;
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
