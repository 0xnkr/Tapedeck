using System;
using System.Globalization;
using Avalonia.Media;

namespace Tapedeck;

/// <summary>
/// Command line options for trying things out without code changes:
/// <c>--lang en</c> overrides the UI language, <c>--accent #C8B6FF</c> the accent color.
/// They win over the saved settings for this run only.
/// <c>--spotify-report &lt;folder|zip&gt;</c> writes a report of a Spotify streaming history and exits,
/// <c>--spotify-match &lt;folder|zip&gt; [count]</c> searches its most played songs on YouTube and reports the matches,
/// <c>--audio-report [count]</c> measures the sound of the library and reports the closest songs by sound,
/// <c>--self-check</c> checks the tools, OpenAL and the Apple export in a temporary folder (for CI on macOS).
/// </summary>
public sealed record StartupOptions(CultureInfo? Language, Color? Accent, string? SpotifyReport = null,
    string? SpotifyMatch = null, int? SpotifyMatchCount = null, int? AudioReport = null, bool SelfCheck = false)
{
    public static StartupOptions Parse(string[] args)
    {
        CultureInfo? language = null;
        Color? accent = null;
        string? spotifyReport = null;
        string? spotifyMatch = null;
        int? spotifyMatchCount = null;
        int? audioReport = null;
        var selfCheck = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--self-check":
                    selfCheck = true;
                    break;
                // the only option with an optional value
                case "--audio-report":
                    audioReport = AudioReportCount(args, ref i);
                    break;
                case var _ when i + 1 >= args.Length:
                    break;
                case "--lang":
                    language = TryGetCulture(args[++i]);
                    break;
                case "--accent" when Color.TryParse(args[i + 1], out var color):
                    accent = color;
                    i++;
                    break;
                case "--spotify-report":
                    spotifyReport = args[++i];
                    break;
                case "--spotify-match":
                    spotifyMatch = args[++i];
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
                    {
                        spotifyMatchCount = count;
                        i++;
                    }
                    break;
            }
        }

        return new StartupOptions(language, accent, spotifyReport, spotifyMatch, spotifyMatchCount, audioReport, selfCheck);
    }

    /// <summary>The count after <c>--audio-report</c>, or the default when none follows.</summary>
    private static int AudioReportCount(string[] args, ref int i)
    {
        if (i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            i++;
            return count;
        }
        return Services.AudioReportCommand.DEFAULT_COUNT;
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
