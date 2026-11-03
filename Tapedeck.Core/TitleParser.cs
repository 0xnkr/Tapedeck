using System.Text.RegularExpressions;

namespace Tapedeck.Core;

/// <summary>
/// Fallback for artist and title: splits YouTube titles of the form <c>Artist - Title</c>
/// and strips typical suffixes such as "(Official Video)" or "[HD]".
/// </summary>
public static partial class TitleParser
{
    // Brackets containing only upload noise. "(feat. X)" or "(Remix)" are kept.
    [GeneratedRegex(
        """\s*[\(\[](?:[^\)\]]*\b(?:official|offizielles?)\b[^\)\]]*|hd|hq|4k|8k|1080p|720p|lyrics?|lyric video|audio|video|music video|visuali[sz]er|explicit|clean|mv|m/v|remastered(?: \d{4})?|\d{4} remaster(?:ed)?)[\)\]]""",
        RegexOptions.IgnoreCase)]
    private static partial Regex NoisePattern();

    [GeneratedRegex(@"\s+[-–—|]\s+")]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    public static (string? Artist, string Title) Parse(string rawTitle)
    {
        var cleaned = Clean(rawTitle);
        var parts = SeparatorPattern().Split(cleaned, 2);
        if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
            return (parts[0].Trim(), parts[1].Trim().Trim('"', '“', '”'));
        return (null, cleaned);
    }

    public static string Clean(string rawTitle)
    {
        var result = NoisePattern().Replace(rawTitle, "");
        return MultiSpace().Replace(result, " ").Trim();
    }
}
