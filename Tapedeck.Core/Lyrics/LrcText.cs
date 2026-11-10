using System.Text.RegularExpressions;

namespace Tapedeck.Core.Lyrics;

/// <summary>Lyrics in the LRC format: lines like <c>[01:23.45] Text</c>, with ID tags like <c>[ar:Artist]</c> above.</summary>
public static partial class LrcText
{
    /// <summary>The ID tag Tapedeck puts into its own <c>.lrc</c> files, so it never touches the user's ones.</summary>
    public const string OWNER_TAG = "[re:Tapedeck]";

    /// <summary>Whether the text has at least one timed line.</summary>
    public static bool IsSynced(string text) => TimePattern().IsMatch(text);

    /// <summary>The text without times and ID tags, for players that show lyrics without timing.</summary>
    public static string ToPlain(string lrc)
    {
        var lines = lrc.Replace("\r\n", "\n").Split('\n')
            .Where(line => !IdTagPattern().IsMatch(line))
            .Select(line => TimePattern().Replace(line, string.Empty).Trim());
        return string.Join('\n', lines).Trim('\n');
    }

    /// <summary>The text with <see cref="OWNER_TAG"/> as its first line, as written into the <c>.lrc</c> file.</summary>
    public static string WithOwnerTag(string lrc) =>
        OWNER_TAG + "\n" + string.Join('\n', lrc.Replace("\r\n", "\n").Split('\n').Where(line => line.Trim() != OWNER_TAG));

    /// <summary>Whether the text was written by Tapedeck: the owner tag within the first lines.</summary>
    public static bool IsOwn(IEnumerable<string> firstLines) => firstLines.Any(line => line.Trim() == OWNER_TAG);

    [GeneratedRegex(@"\[\d{1,3}:\d{2}(?:[.:]\d{1,3})?\]")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"^\s*\[[a-zA-Z#]+:[^\]]*\]\s*$")]
    private static partial Regex IdTagPattern();
}
