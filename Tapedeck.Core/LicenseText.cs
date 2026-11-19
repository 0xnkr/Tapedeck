using System.Text;
using System.Text.RegularExpressions;

namespace Tapedeck.Core;

/// <summary>License texts for reading on any width.</summary>
public static partial class LicenseText
{
    // "- item", "* item", "1. item", "(a) item", "a) item"
    [GeneratedRegex(@"^([-*•]|\d+[.)]|\(?[A-Za-z0-9]{1,3}\))\s")]
    private static partial Regex ListItem();

    /// <summary>
    /// Joins the lines of each paragraph, which the license files break after about 80 characters: on a narrow screen
    /// those breaks cut every line in two. Empty lines, list items and lines indented less than their paragraph stay
    /// where they are, so headings, lists and sections keep their shape.
    /// </summary>
    public static string Reflow(string text)
    {
        var result = new StringBuilder();
        var indent = -1;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            var lineIndent = line.Length - line.TrimStart().Length;
            if (trimmed.Length == 0)
            {
                // ends the paragraph's line, then the empty line itself
                if (indent >= 0)
                    result.Append('\n');
                result.Append('\n');
                indent = -1;
                continue;
            }

            if (indent >= 0 && lineIndent >= indent && !ListItem().IsMatch(trimmed))
            {
                result.Append(' ').Append(trimmed);
                continue;
            }

            if (indent >= 0)
                result.Append('\n');
            result.Append(line.TrimEnd());
            // continuation lines of a list item are usually indented below its text, not below its marker
            indent = lineIndent;
        }
        return result.ToString().TrimEnd('\n');
    }
}
