using Tapedeck.Core.Models;
using Tapedeck.Core.Playlists;

namespace Tapedeck.Tests;

public class SmartRulesTests
{
    private static readonly DateTime NOW = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static TrackDisplay Track(long id, string artist, string title, string? album = null, int? year = null,
        string? genres = null, int? rating = null, string addedAt = "2026-01-01T00:00:00Z", int? trackNo = null) => new()
    {
        Id = id, Artist = artist, Title = title, Album = album, AlbumArtist = artist, Year = year, Genres = genres,
        Rating = rating, AddedAt = addedAt, TrackNo = trackNo,
    };

    private static readonly TrackDisplay[] LIBRARY =
    [
        Track(1, "Kendrick Lamar", "Money Trees", "good kid, m.A.A.d city", 2012, "Hip Hop;West Coast Hip Hop", 5, "2026-11-01T10:00:00Z", 5),
        Track(2, "Kendrick Lamar", "Swimming Pools (Drank)", "good kid, m.A.A.d city", 2012, "Hip Hop", 4, "2026-10-01T10:00:00Z", 9),
        Track(3, "Wiz Khalifa", "We Dem Boyz", "Blacc Hollywood", 2014, "Hip Hop;Trap", 3, "2026-11-05T10:00:00Z"),
        Track(4, "Juicy J", "For Everybody", null, 2015, "Memphis Rap;Trap", null, "2026-09-01T10:00:00Z"),
        Track(5, "Beyoncé", "AMERICA HAS A PROBLEM", "RENAISSANCE", 2022, "R&B", 4, "2026-08-01T10:00:00Z"),
    ];

    private static IReadOnlyList<long> Ids(SmartRules rules) => rules.Apply(LIBRARY, NOW).Select(t => t.Id).ToList();

    private static SmartRules All(params SmartRule[] rules) => new(SmartMatch.All, rules, SmartSort.Newest, null);

    [Fact]
    public void Genre_contains_looks_at_every_genre_of_a_track() =>
        Assert.Equal([3, 4], Ids(All(new SmartRule(SmartField.Genre, SmartOperator.Contains, "trap"))));

    [Fact]
    public void Main_genre_looks_at_the_main_genres_the_fine_ones_map_to()
    {
        Assert.Equal([1, 2, 3, 4], Ids(All(new SmartRule(SmartField.MainGenre, SmartOperator.Is, "hip-hop"))).Order());
        Assert.Equal([5], Ids(All(new SmartRule(SmartField.MainGenre, SmartOperator.Is, "R&B"))));
    }

    [Fact]
    public void Text_ignores_case_and_accents() =>
        Assert.Equal([5], Ids(All(new SmartRule(SmartField.Artist, SmartOperator.Is, "beyonce"))));

    [Fact]
    public void Numbers_compare_and_tracks_without_value_only_match_is_not()
    {
        Assert.Equal([3, 5], Ids(All(new SmartRule(SmartField.Year, SmartOperator.AtLeast, "2014"),
            new SmartRule(SmartField.Rating, SmartOperator.AtLeast, "3"))));
        Assert.Contains(4L, Ids(All(new SmartRule(SmartField.Rating, SmartOperator.IsNot, "5"))));
        Assert.DoesNotContain(4L, Ids(All(new SmartRule(SmartField.Rating, SmartOperator.AtMost, "2"))));
    }

    [Fact]
    public void Any_needs_only_one_rule() =>
        Assert.Equal([3, 4, 5], Ids(new SmartRules(SmartMatch.Any,
        [
            new SmartRule(SmartField.Genre, SmartOperator.Is, "Trap"),
            new SmartRule(SmartField.Album, SmartOperator.Is, "RENAISSANCE"),
        ], SmartSort.Newest, null)));

    [Fact]
    public void Added_within_days_uses_the_given_clock() =>
        Assert.Equal([3, 1], Ids(All(new SmartRule(SmartField.Added, SmartOperator.WithinDays, "7"))));

    [Fact]
    public void Not_contains_keeps_tracks_without_that_genre() =>
        Assert.Equal([1, 2, 5], Ids(All(new SmartRule(SmartField.Genre, SmartOperator.NotContains, "trap"))));

    [Fact]
    public void No_rules_sorted_and_limited_gives_the_newest() =>
        Assert.Equal([3, 1], Ids(new SmartRules(SmartMatch.All, [], SmartSort.Newest, 2)));

    [Fact]
    public void Album_sort_follows_the_track_numbers_and_puts_tracks_without_album_last() =>
        Assert.Equal([3, 1, 2, 5, 4], Ids(new SmartRules(SmartMatch.All, [], SmartSort.Album, null)));

    [Fact]
    public void Rule_with_a_number_field_and_no_number_matches_nothing() =>
        Assert.Empty(Ids(All(new SmartRule(SmartField.Year, SmartOperator.Is, "soon"))));

    [Fact]
    public void Json_round_trips_and_broken_json_gives_no_rules()
    {
        var rules = new SmartRules(SmartMatch.Any, [new SmartRule(SmartField.Genre, SmartOperator.Contains, "Trap")], SmartSort.Year, 50);
        Assert.Equal(rules.ToJson(), SmartRules.FromJson(rules.ToJson()).ToJson());
        Assert.Contains("\"genre\"", rules.ToJson());
        Assert.Empty(SmartRules.FromJson("{ broken").Rules);
    }

    [Fact]
    public void Every_field_offers_operators() =>
        Assert.All(Enum.GetValues<SmartField>(), f => Assert.NotEmpty(SmartRules.OperatorsFor(f)));
}
