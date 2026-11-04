using System.Text;

namespace Tapedeck.Core;

/// <summary>
/// File names for the flat library: <c>Artist - Title.ext</c>.
/// Must be valid on FAT32/exFAT (Android, SD cards) and all desktop systems.
/// </summary>
public static class FileNaming
{
    public const int MAX_BASE_LENGTH = 150;

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
    /// Like <see cref="Choose"/>, but keeps the file a track already owns, so a repeated download
    /// overwrites it instead of treating it as a collision and leaving the old file behind.
    /// </summary>
    public static string ChooseForTrack(string? currentFile, string artist, string title, string videoId, string extension,
        Func<string, bool> isTaken)
    {
        var ext = extension.TrimStart('.');
        if (currentFile is not null && currentFile.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase))
            return currentFile;
        return Choose(artist, title, videoId, ext, isTaken);
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
