using System.Globalization;
using System.Text;

namespace Tapedeck.Core;

/// <summary>
/// File names for the flat library: <c>Artist - Title.ext</c>.
/// Must be valid on FAT32/exFAT (Android, SD cards) and all desktop systems.
/// </summary>
public static class FileNaming
{
    public const int MAX_BASE_LENGTH = 150;

    // slug of a name without a single letter or digit
    private const string SLUG_FALLBACK = "playlist";

    public static string BaseName(string artist, string title)
    {
        var name = string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}";
        return Sanitize(name);
    }

    /// <summary>
    /// Returns an unused file name. If <c>Artist - Title.ext</c> is taken,
    /// the video ID is appended: <c>Artist - Title [id].ext</c>.
    /// </summary>
    public static string Choose(string artist, string title, string videoId, string extension, Func<string, bool> isTaken)
    {
        var ext = extension.TrimStart('.');
        var baseName = BaseName(artist, title);
        var plain = $"{baseName}.{ext}";
        if (!isTaken(plain))
            return plain;
        return $"{Truncate(baseName, MAX_BASE_LENGTH - videoId.Length - 3)} [{videoId}].{ext}";
    }

    /// <summary>
    /// The name a track's file should have now. Keeps the current file when its name still fits artist and title
    /// (also the <c>[id]</c> variant), so a repeated download overwrites it instead of treating it as a collision.
    /// After a correction it returns the new name; the track's own file never counts as taken.
    /// </summary>
    public static string ChooseForTrack(string? currentFile, string artist, string title, string videoId, string extension,
        Func<string, bool> isTaken)
    {
        var ext = extension.TrimStart('.');
        var baseName = BaseName(artist, title);
        var fitting = new[]
        {
            $"{baseName}.{ext}",
            $"{Truncate(baseName, MAX_BASE_LENGTH - videoId.Length - 3)} [{videoId}].{ext}",
        };
        if (currentFile is not null && fitting.Contains(currentFile, StringComparer.OrdinalIgnoreCase))
            return currentFile;
        return Choose(artist, title, videoId, ext,
            name => !string.Equals(name, currentFile, StringComparison.OrdinalIgnoreCase) && isTaken(name));
    }

    /// <summary>
    /// A short ASCII form of a name for file names like <c>Tapedeck-road-trip.csv</c>: lower case, German umlauts
    /// spelled out, other accents dropped, everything else becomes a single <c>-</c>.
    /// </summary>
    public static string Slug(string name)
    {
        var spelled = name.ToLowerInvariant()
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss")
            .Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(spelled.Length);
        foreach (var c in spelled)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                sb.Append(c);
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        var slug = sb.ToString().TrimEnd('-');
        return slug.Length > 0 ? slug : SLUG_FALLBACK;
    }

    /// <summary>A readable name from a slug or file name: <c>road-trip</c> becomes <c>Road Trip</c>.</summary>
    public static string NameFromSlug(string slug)
    {
        var words = slug.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    public static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsControl(c))
                continue;
            sb.Append(c switch
            {
                ':' or '|' or '/' or '\\' => " - ",
                '"' => "'",
                '<' or '>' or '?' or '*' => string.Empty,
                _ => c.ToString(),
            });
        }

        var collapsed = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        while (collapsed.Contains(" - - "))
            collapsed = collapsed.Replace(" - - ", " - ");

        var result = Truncate(collapsed, MAX_BASE_LENGTH).Trim(' ', '-').TrimEnd('.');
        return result.Length == 0 ? "Untitled" : result;
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max)
            return s;
        var cut = s[..max];
        // don't leave half a surrogate pair at the end
        if (char.IsHighSurrogate(cut[^1]))
            cut = cut[..^1];
        return cut.TrimEnd('.', ' ');
    }
}
