using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tapedeck.Core.Models;

namespace Tapedeck.Core.Playlists;

/// <summary>A value of a track a smart playlist rule looks at; always the shown value of <c>tracks_display</c>.</summary>
public enum SmartField
{
    Artist,
    Title,
    Album,
    AlbumArtist,
    /// <summary>Any of the track's genres.</summary>
    Genre,
    /// <summary>Any of the track's main genres (see <see cref="Genres.GenreMap"/>), as written into the file.</summary>
    MainGenre,
    Year,
    /// <summary>1 to 5 stars; unrated tracks have no value.</summary>
    Rating,
    /// <summary>When the track was added to the library.</summary>
    Added,
}

public enum SmartOperator
{
    Contains,
    NotContains,
    Is,
    IsNot,
    AtLeast,
    AtMost,
    /// <summary>For <see cref="SmartField.Added"/>: within the last N days.</summary>
    WithinDays,
}

/// <summary>Whether a track has to match all rules or one is enough.</summary>
public enum SmartMatch
{
    All,
    Any,
}

public enum SmartSort
{
    /// <summary>Last added first.</summary>
    Newest,
    /// <summary>By artist, then album and track number.</summary>
    Artist,
    /// <summary>By album and track number, like the album plays.</summary>
    Album,
    /// <summary>Newest release year first.</summary>
    Year,
    /// <summary>Best rated first.</summary>
    Rating,
}

/// <param name="Value">As typed by the user; numbers for year, rating and days.</param>
public sealed record SmartRule(SmartField Field, SmartOperator Operator, string Value);

/// <summary>
/// The rules of a smart playlist, stored as JSON in <c>playlists.rules</c>. The playlist's tracks are not stored:
/// <see cref="Apply"/> picks them from the library whenever they are needed, so the playlist stays current.
/// </summary>
/// <param name="Rules">No rules: every track, e.g. "the 50 newest".</param>
/// <param name="Limit">At most this many tracks after sorting; <c>null</c> for all.</param>
public sealed record SmartRules(SmartMatch Match, IReadOnlyList<SmartRule> Rules, SmartSort Sort, int? Limit)
{
    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly CompareInfo COMPARE = CultureInfo.InvariantCulture.CompareInfo;

    // "beyonce" finds "Beyoncé", "TRAP" finds "Trap"
    private const CompareOptions TEXT_OPTIONS = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public static SmartRules Empty => new(SmartMatch.All, [], SmartSort.Newest, null);

    /// <summary>The operators that make sense for a field; the first one is the default.</summary>
    public static IReadOnlyList<SmartOperator> OperatorsFor(SmartField field) => field switch
    {
        SmartField.Year or SmartField.Rating => [SmartOperator.Is, SmartOperator.AtLeast, SmartOperator.AtMost, SmartOperator.IsNot],
        SmartField.Added => [SmartOperator.WithinDays],
        _ => [SmartOperator.Contains, SmartOperator.Is, SmartOperator.NotContains, SmartOperator.IsNot],
    };

    /// <summary>Fields compared as whole numbers.</summary>
    public static bool IsNumeric(SmartField field) => field is SmartField.Year or SmartField.Rating or SmartField.Added;

    /// <summary>The rule's value as a number, for numeric fields; <c>null</c> when it is none.</summary>
    public static int? NumberOf(SmartRule rule) =>
        int.TryParse(rule.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>The tracks of the playlist: matching, sorted and cut to the limit.</summary>
    /// <param name="nowUtc">For "added within the last days"; passed in so tests do not depend on the clock.</param>
    public IReadOnlyList<TrackDisplay> Apply(IEnumerable<TrackDisplay> tracks, DateTime nowUtc)
    {
        var matching = tracks.Where(t => this.Matches(t, nowUtc));
        var sorted = this.SortBy(matching);
        return (this.Limit is { } limit and >= 0 ? sorted.Take(limit) : sorted).ToList();
    }

    public bool Matches(TrackDisplay track, DateTime nowUtc)
    {
        if (this.Rules.Count == 0)
            return true;
        return this.Match == SmartMatch.All
            ? this.Rules.All(r => MatchesRule(track, r, nowUtc))
            : this.Rules.Any(r => MatchesRule(track, r, nowUtc));
    }

    public string ToJson() => JsonSerializer.Serialize(this, JSON_OPTIONS);

    /// <summary>A broken value gives no rules, so the playlist shows every track rather than failing.</summary>
    public static SmartRules FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty;
        try
        {
            var rules = JsonSerializer.Deserialize<SmartRules>(json, JSON_OPTIONS);
            return rules is null ? Empty : rules with { Rules = rules.Rules ?? [] };
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static bool MatchesRule(TrackDisplay track, SmartRule rule, DateTime nowUtc)
    {
        if (rule.Field == SmartField.Added)
            return AddedWithin(track, rule, nowUtc);
        if (IsNumeric(rule.Field))
            return MatchesNumber(NumberOf(track, rule.Field), rule);

        var texts = TextsOf(track, rule.Field);
        var value = rule.Value.Trim();
        return rule.Operator switch
        {
            SmartOperator.Contains => texts.Any(t => COMPARE.IndexOf(t, value, TEXT_OPTIONS) >= 0),
            SmartOperator.NotContains => !texts.Any(t => COMPARE.IndexOf(t, value, TEXT_OPTIONS) >= 0),
            SmartOperator.Is => texts.Any(t => COMPARE.Compare(t.Trim(), value, TEXT_OPTIONS) == 0),
            SmartOperator.IsNot => !texts.Any(t => COMPARE.Compare(t.Trim(), value, TEXT_OPTIONS) == 0),
            _ => false,
        };
    }

    /// <summary>A track without a value (unrated, no year) only matches "is not".</summary>
    private static bool MatchesNumber(int? actual, SmartRule rule)
    {
        if (NumberOf(rule) is not { } wanted)
            return false;
        if (actual is not { } value)
            return rule.Operator == SmartOperator.IsNot;
        return rule.Operator switch
        {
            SmartOperator.Is => value == wanted,
            SmartOperator.IsNot => value != wanted,
            SmartOperator.AtLeast => value >= wanted,
            SmartOperator.AtMost => value <= wanted,
            _ => false,
        };
    }

    private static bool AddedWithin(TrackDisplay track, SmartRule rule, DateTime nowUtc) =>
        NumberOf(rule) is { } days
        && DateTime.TryParse(track.AddedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var added)
        && added >= nowUtc.AddDays(-days);

    private static IReadOnlyList<string> TextsOf(TrackDisplay track, SmartField field) => field switch
    {
        SmartField.Artist => [track.Artist],
        SmartField.Title => [track.Title],
        SmartField.Album => track.Album is { } album ? [album] : [],
        SmartField.AlbumArtist => [track.AlbumArtist],
        SmartField.Genre => track.GenreList,
        SmartField.MainGenre => track.MainGenreList,
        _ => [],
    };

    private static int? NumberOf(TrackDisplay track, SmartField field) => field switch
    {
        SmartField.Year => track.Year,
        SmartField.Rating => track.Rating,
        _ => null,
    };

    private IEnumerable<TrackDisplay> SortBy(IEnumerable<TrackDisplay> tracks) => this.Sort switch
    {
        SmartSort.Artist => tracks
            .OrderBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Album ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.DiscNo ?? 0).ThenBy(t => t.TrackNo ?? 0)
            .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase),
        SmartSort.Album => tracks
            .OrderBy(t => t.Album is null)
            .ThenBy(t => t.Album ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.AlbumArtist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.DiscNo ?? 0).ThenBy(t => t.TrackNo ?? 0)
            .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase),
        SmartSort.Year => tracks
            .OrderBy(t => t.Year is null)
            .ThenByDescending(t => t.Year)
            .ThenBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase),
        SmartSort.Rating => tracks
            .OrderByDescending(t => t.Rating ?? 0)
            .ThenByDescending(t => t.AddedAt, StringComparer.Ordinal),
        _ => tracks
            .OrderByDescending(t => t.AddedAt, StringComparer.Ordinal)
            .ThenByDescending(t => t.Id),
    };
}
