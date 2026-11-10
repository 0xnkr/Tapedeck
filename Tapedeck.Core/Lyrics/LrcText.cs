using System.Globalization;
using System.Text.RegularExpressions;

namespace Tapedeck.Core.Lyrics;

/// <summary>One timed line of LRC lyrics; empty text is a pause in the singing.</summary>
public sealed record LrcLine(TimeSpan Time, string Text);

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

    /// <summary>
    /// The timed lines in playing order. A line with several times (a repeated chorus) comes once per time, an
    /// empty line marks a pause in the singing. <c>[offset:+500]</c> shows every line half a second sooner.
    /// </summary>
    public static IReadOnlyList<LrcLine> Parse(string lrc)
    {
        var lines = new List<LrcLine>();
        var offset = TimeSpan.Zero;
        foreach (var raw in lrc.Replace("\r\n", "\n").Split('\n'))
        {
            if (OffsetPattern().Match(raw) is { Success: true } offsetTag)
            {
                offset = TimeSpan.FromMilliseconds(int.Parse(offsetTag.Groups[1].Value, CultureInfo.InvariantCulture));
                continue;
            }

            var times = new List<TimeSpan>();
            var rest = raw.TrimStart();
            while (LeadingTimePattern().Match(rest) is { Success: true } time)
            {
                times.Add(TimeOf(time));
                rest = rest[time.Length..].TrimStart();
            }
            lines.AddRange(times.Select(t => new LrcLine(t, rest.TrimEnd())));
        }

        return lines
            .Select(l => l with { Time = l.Time - offset < TimeSpan.Zero ? TimeSpan.Zero : l.Time - offset })
            .OrderBy(l => l.Time)
            .ToList();
    }

    /// <summary>The index of the line sung at <paramref name="position"/>; -1 before the first line.</summary>
    public static int IndexAt(IReadOnlyList<LrcLine> lines, TimeSpan position)
    {
        int low = 0, high = lines.Count - 1, found = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (lines[middle].Time <= position)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return found;
    }

    // "[01:23.45]": minutes, seconds and a fraction of one to three digits (.5 is half a second)
    private static TimeSpan TimeOf(Match time)
    {
        var minutes = int.Parse(time.Groups[1].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(time.Groups[2].Value, CultureInfo.InvariantCulture);
        var fraction = time.Groups[3].Value;
        var milliseconds = fraction.Length == 0 ? 0 : int.Parse(fraction.PadRight(3, '0'), CultureInfo.InvariantCulture);
        return new TimeSpan(0, 0, minutes, seconds, milliseconds);
    }

    [GeneratedRegex(@"\[\d{1,3}:\d{2}(?:[.:]\d{1,3})?\]")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"^\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex LeadingTimePattern();

    [GeneratedRegex(@"^\s*\[offset:\s*([+-]?\d+)\s*\]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetPattern();

    [GeneratedRegex(@"^\s*\[[a-zA-Z#]+:[^\]]*\]\s*$")]
    private static partial Regex IdTagPattern();
}
