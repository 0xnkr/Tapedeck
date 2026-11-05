using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tapedeck.Core.MusicBrainz;

/// <summary>Versions of a song that are not the song itself; a match must agree on them.</summary>
[Flags]
public enum MbVariant
{
    None = 0,
    Live = 1 << 0,
    Remix = 1 << 1,
    Instrumental = 1 << 2,
    Acapella = 1 << 3,
    Edit = 1 << 4,
    Slowed = 1 << 5,
    SpedUp = 1 << 6,
    Acoustic = 1 << 7,
    Demo = 1 << 8,
    SignLanguage = 1 << 9,
    Karaoke = 1 << 10,
    /// <summary>Only part of a DJ mix or megamix; a YouTube video is never that.</summary>
    DjMix = 1 << 11,
    /// <summary>A later recording of the song by the same artist.</summary>
    ReRecorded = 1 << 12,
}

/// <summary>
/// Text helpers for comparing YouTube and MusicBrainz values: normalization, guests, variants, similarity.
/// </summary>
public static partial class MbNormalizer
{
    [GeneratedRegex(@"\s*[\(\[]?\s*\b(?:feat\.?|ft\.?|featuring|f\.)\s+([^\(\)\[\]]*)[\)\]]?", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturingPattern();

    [GeneratedRegex(@"\s*(?:,|&|\+|/|\band\b|\bx\b)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSeparator();

    [GeneratedRegex(@"[\(\[\{][^\)\]\}]*[\)\]\}]?")]
    private static partial Regex BracketPattern();

    [GeneratedRegex(@"\s+[-–—|]\s+")]
    private static partial Regex DashPattern();

    [GeneratedRegex(@"\b(?:deluxe(?: edition| version)?|collectors? edition|expanded(?: edition)?|explicit(?: version)?|remaster(?:ed)?|anniversary edition|special edition)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EditionPattern();

    [GeneratedRegex(@"(?:vevo|\s-\stopic)$", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelSuffix();

    private static readonly (MbVariant Variant, Regex Pattern)[] VARIANTS =
    [
        (MbVariant.Live, new Regex(@"\blive\b", RegexOptions.Compiled)),
        (MbVariant.Remix, new Regex(@"\b(?:remix(?:ed)?|rmx|bootleg|rework|revision|flip|vip|mashup)\b", RegexOptions.Compiled)),
        (MbVariant.Instrumental, new Regex(@"\binstrumental\b", RegexOptions.Compiled)),
        (MbVariant.Acapella, new Regex(@"\b(?:a ?cappella|acapella)\b", RegexOptions.Compiled)),
        (MbVariant.Edit, new Regex(@"\bedit\b", RegexOptions.Compiled)),
        (MbVariant.Slowed, new Regex(@"\b(?:slowed|chopped|screwed|reverb)\b", RegexOptions.Compiled)),
        (MbVariant.SpedUp, new Regex(@"\b(?:sped up|speed up|nightcore)\b", RegexOptions.Compiled)),
        (MbVariant.Acoustic, new Regex(@"\b(?:acoustic|unplugged)\b", RegexOptions.Compiled)),
        (MbVariant.Demo, new Regex(@"\bdemo\b", RegexOptions.Compiled)),
        (MbVariant.SignLanguage, new Regex(@"\b(?:sign language|asl)\b", RegexOptions.Compiled)),
        (MbVariant.Karaoke, new Regex(@"\bkaraoke\b", RegexOptions.Compiled)),
        (MbVariant.DjMix, new Regex(@"\b(?:part of|dj mix|megamix)\b", RegexOptions.Compiled)),
        (MbVariant.ReRecorded, new Regex(@"\b(?:re recorded|rerecorded|re record|taylors version)\b", RegexOptions.Compiled)),
    ];

    // words of YouTube titles that never help to find a song
    private static readonly HashSet<string> NOISE_WORDS =
    [
        "official", "music", "video", "audio", "lyrics", "lyric", "visualizer", "hd", "hq", "4k", "mv",
        "explicit", "clean", "feat", "ft", "f", "featuring", "prod", "and", "or", "not", "to",
    ];

    /// <summary>
    /// Lowercase, without accents and punctuation: "Gangsta’s Paradise" and "Gangsta's Paradise" become equal,
    /// "Ty Dolla $ign" equals "Ty Dolla Sign", "&amp;" counts as "and". A leading "the" is dropped.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            switch (c)
            {
                case '&':
                    builder.Append(" and ");
                    break;
                case '$':
                    builder.Append('s');
                    break;
                // apostrophes join the word: "gangsta's" -> "gangstas"
                case '\'' or '’' or '‘' or '´' or '`':
                    break;
                default:
                    builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
                    break;
            }
        }

        var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var start = words.Length > 1 && words[0] == "the" ? 1 : 0;
        return string.Join(' ', words.Skip(start));
    }

    /// <summary>Splits "Song (feat. A &amp; B)" or "Artist ft. A" into the main part and the guests.</summary>
    public static (string Main, IReadOnlyList<string> Guests) SplitFeaturing(string text)
    {
        var match = FeaturingPattern().Match(text);
        if (!match.Success)
            return (text.Trim(), []);

        var main = (text[..match.Index] + " " + text[(match.Index + match.Length)..]).Trim(' ', '-', '–', ',');
        return (CollapseSpaces(main), SplitArtists(match.Groups[1].Value));
    }

    /// <summary>"Kendrick Lamar, SZA" or "Snoop Dogg &amp; Wiz Khalifa" into single names.</summary>
    public static IReadOnlyList<string> SplitArtists(string text) =>
        ArtistSeparator().Split(text)
            .Select(a => a.Trim(' ', '"', '“', '”', '(', ')', '[', ']', '-'))
            .Where(a => a.Length > 0)
            .ToList();

    /// <summary>The song name without brackets and dash suffixes: "Venom (Music From The Motion Picture)" → "Venom".</summary>
    public static string Core(string title)
    {
        var withoutGuests = SplitFeaturing(title).Main;
        var withoutBrackets = BracketPattern().Replace(withoutGuests, " ");
        var core = DashPattern().Split(withoutBrackets)[0];
        core = CollapseSpaces(core.Trim(' ', '"', '“', '”', '(', ')', '[', ']', '-'));
        return core.Length > 0 ? core : withoutGuests.Trim();
    }

    /// <summary>Everything besides the core: bracket contents and dash suffixes, where versions like "Live" are named.</summary>
    public static string Qualifiers(string title)
    {
        var withoutGuests = SplitFeaturing(title).Main;
        var brackets = BracketPattern().Matches(withoutGuests).Select(m => m.Value);
        var dashParts = DashPattern().Split(BracketPattern().Replace(withoutGuests, " ")).Skip(1);
        return string.Join(' ', brackets.Concat(dashParts));
    }

    public static MbVariant Variants(string? text)
    {
        var normalized = Normalize(text);
        var result = MbVariant.None;
        foreach (var (variant, pattern) in VARIANTS)
        {
            if (pattern.IsMatch(normalized))
                result |= variant;
        }
        return result;
    }

    /// <summary>Album title for comparing: without "(Deluxe)", "Collectors Edition", "Explicit" and the like.</summary>
    public static string Edition(string? album) =>
        Normalize(EditionPattern().Replace(BracketPattern().Replace(album ?? string.Empty, " "), " "));

    /// <summary>"KendrickLamarVEVO" → "KendrickLamar", "Coolio - Topic" → "Coolio".</summary>
    public static string CleanChannel(string channel) => ChannelSuffix().Replace(channel.Trim(), string.Empty).Trim();

    /// <summary>1 for equal texts after <see cref="Normalize"/>, otherwise the better of word overlap and edit distance.</summary>
    public static double Similarity(string? a, string? b) => SimilarityNormalized(Normalize(a), Normalize(b));

    public static double SimilarityNormalized(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
            return 0;
        if (a == b)
            return 1;

        var wordsA = a.Split(' ').ToHashSet();
        var wordsB = b.Split(' ').ToHashSet();
        var jaccard = (double)wordsA.Intersect(wordsB).Count() / wordsA.Union(wordsB).Count();

        var squashedA = a.Replace(" ", string.Empty);
        var squashedB = b.Replace(" ", string.Empty);
        var edit = squashedA == squashedB
            ? 1
            : 1 - (double)Levenshtein(squashedA, squashedB) / Math.Max(squashedA.Length, squashedB.Length);

        return Math.Max(jaccard, edit);
    }

    /// <summary>Whether <paramref name="needle"/> occurs as whole words in <paramref name="text"/> (both normalized).</summary>
    public static bool ContainsWords(string text, string needle) =>
        needle.Length > 0 && $" {text} ".Contains($" {needle} ", StringComparison.Ordinal);

    /// <summary>Removes every whole-word occurrence of <paramref name="needle"/> (both normalized).</summary>
    public static string RemoveWords(string text, string needle) =>
        needle.Length == 0 ? text : CollapseSpaces($" {text} ".Replace($" {needle} ", " ", StringComparison.Ordinal));

    /// <summary>Escapes a value for a quoted Lucene phrase.</summary>
    public static string EscapePhrase(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Search words of a raw YouTube title: letters and digits only, without words like "official" or "feat".</summary>
    public static IReadOnlyList<string> SearchWords(string rawTitle) =>
        Normalize(rawTitle).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !NOISE_WORDS.Contains(w))
            .Distinct()
            .ToList();

    private static string CollapseSpaces(string text) =>
        string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
