using Tapedeck.Core.Models;
using Tapedeck.Core.MusicBrainz;

namespace Tapedeck.Tests;

/// <summary>
/// Real MusicBrainz search responses (<c>Fixtures/MusicBrainz</c>, reduced to the fields Tapedeck reads)
/// against tracks as they come from YouTube. No network access.
/// </summary>
public class MbMatcherTests
{
    private static IReadOnlyList<MbRecording> Fixture(string name) =>
        MbJson.ParseRecordingSearch(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MusicBrainz", name + ".json")));

    /// <summary>A "Topic" upload of YouTube Music: track, artist and album are set.</summary>
    private static MbQuery Topic(string track, string artist, string? album, int duration) =>
        MbQuery.From(new Track { YtTrack = track, YtArtist = artist, YtAlbum = album, DurationS = duration, YtTitle = track })!;

    /// <summary>A music video: only the title, split by TitleParser.</summary>
    private static MbQuery Video(string rawTitle, int duration, string? channel = null)
    {
        var (artist, title) = Tapedeck.Core.TitleParser.Parse(rawTitle);
        return MbQuery.From(new Track
        {
            YtTitle = rawTitle, ParsedArtist = artist, ParsedTitle = title, Channel = channel, DurationS = duration,
        })!;
    }

    private static MbMatchResult Strict(string fixture, MbQuery query) => MbMatcher.Match(query, Fixture(fixture), fallback: false);

    [Fact]
    public void Topic_upload_matches_the_album_not_live_versions()
    {
        var result = Strict("money-trees", Topic("Money Trees", "Kendrick Lamar", "good kid, m.A.A.d city", 387));
        Assert.Equal(MbDecision.Matched, result.Decision);
        Assert.Equal("good kid, m.A.A.d city", result.Best!.Pick.Album);
        Assert.StartsWith("2012", result.Best.Pick.Date);
    }

    [Fact]
    public void Soundtrack_album_beats_the_single_youtube_names()
    {
        var result = Strict("lose-yourself", Topic("Lose Yourself", "Eminem", "Just Lose It", 322));
        Assert.Equal(MbDecision.Matched, result.Decision);
        Assert.StartsWith("8 Mile", result.Best!.Pick.Album);
        Assert.StartsWith("2002", result.Best.Pick.Date);
    }

    [Fact]
    public void Original_album_beats_compilations_and_reissues()
    {
        var result = Strict("gangstas-paradise", Topic("Gangsta's Paradise", "Coolio", "Gangsta's Paradise", 241));
        Assert.Equal(MbDecision.Matched, result.Decision);
        Assert.Equal("Gangsta’s Paradise", result.Best!.Pick.Album);
        Assert.StartsWith("1995", result.Best.Pick.Date);
    }

    [Fact]
    public void Popular_song_finds_the_album_version_among_many_recordings()
    {
        var result = Strict("in-da-club", Topic("In Da Club", "50 Cent", "Get Rich Or Die Tryin'", 193));
        Assert.Equal(MbDecision.Matched, result.Decision);
        Assert.StartsWith("Get Rich or Die Tryin", result.Best!.Pick.Album);
        Assert.StartsWith("2003", result.Best.Pick.Date);
    }

    [Theory]
    [InlineData("alright", "Kendrick Lamar - Alright", 415, "To Pimp a Butterfly")]
    [InlineData("dna", "Kendrick Lamar - DNA.", 286, "DAMN.")]
    public void Music_video_gets_the_studio_album(string fixture, string rawTitle, int duration, string album)
    {
        var result = Strict(fixture, Video(rawTitle, duration));
        Assert.Equal(MbDecision.Matched, result.Decision);
        Assert.Equal(album, result.Best!.Pick.Album);
    }

    [Fact]
    public void Collectors_edition_falls_back_to_the_original_album()
    {
        var result = Strict("humble", Topic("HUMBLE.", "Kendrick Lamar", "DAMN. COLLECTORS EDITION.", 177));
        Assert.Equal(MbDecision.Matched, result.Decision);
        Assert.Equal("DAMN.", result.Best!.Pick.Album);
    }

    [Fact]
    public void Guest_in_the_title_still_matches_the_main_artist()
    {
        var result = Strict("collard-greens", Video("SchoolBoy Q - Collard Greens ft. Kendrick Lamar", 284));
        Assert.Equal(MbDecision.Matched, result.Decision);
        Assert.Equal("Oxymoron", result.Best!.Pick.Album);
        Assert.Contains("Kendrick Lamar", result.Best.Pick.Artist);
    }

    [Fact]
    public void Co_artist_credit_matches_and_album_artist_comes_from_the_release()
    {
        var result = Strict("luther", Topic("luther", "Kendrick Lamar, SZA", null, 311));
        Assert.Equal(MbDecision.Matched, result.Decision);
        Assert.Equal("GNX", result.Best!.Pick.Album);
        Assert.Equal("Kendrick Lamar", result.Best.Pick.AlbumArtist);
    }

    [Fact]
    public void Remix_in_the_title_only_matches_remix_recordings()
    {
        var result = Strict("pimp-remix", Topic("P.I.M.P. (Snoop Dogg Remix)", "50 Cent", "Get Rich Or Die Tryin'", 289));
        Assert.NotEqual(MbDecision.NoMatch, result.Decision);
        Assert.All(result.Candidates, c => Assert.True(
            MbMatcher.Variants(Fixture("pimp-remix").First(r => r.Id == c.Pick.RecordingId)).HasFlag(MbVariant.Remix)));
    }

    [Fact]
    public void Revision_remix_is_not_the_original()
    {
        var result = Strict("this-is-america", Topic("This Is America", "Childish Gambino", "This Is America", 226));
        Assert.Equal(MbDecision.Matched, result.Decision);
        Assert.Equal("This Is America", result.Best!.Pick.Title);
    }

    [Fact]
    public void Soundtrack_beats_a_various_artists_collection_entered_as_album()
    {
        var result = Strict("all-the-stars", Video("Kendrick Lamar, SZA - All The Stars", 234));
        Assert.Equal(MbDecision.Matched, result.Decision);
        Assert.StartsWith("Black Panther", result.Best!.Pick.Album);
    }

    [Fact]
    public void Lead_of_exactly_a_tenth_still_matches()
    {
        var result = Strict("shake-that", Topic("Shake That", "Eminem", "Curtain Call: The Hits", 274));
        Assert.Equal(MbDecision.Matched, result.Decision);
    }

    [Fact]
    public void Sign_language_video_finds_nothing()
    {
        var result = Strict("alright", Video("Kendrick Lamar - Alright (Sign Language)", 415));
        Assert.Equal(MbDecision.NoMatch, result.Decision);
    }

    [Fact]
    public void Snippet_is_only_a_suggestion()
    {
        var result = Strict("money-trees", Topic("Money Trees", "Kendrick Lamar", null, 100));
        Assert.Equal(MbDecision.Suggested, result.Decision);
    }

    [Fact]
    public void Empty_search_is_no_match() =>
        Assert.Equal(MbDecision.NoMatch, Strict("no-results", Topic("Unknown Song", "Juicy J", null, 115)).Decision);

    [Theory]
    [InlineData("whole-thang-fallback", "Juicy J \"Whole Thang\" feat. Wiz Khalifa", "WORLDSTARHIPHOP", 255, "Whole Thang")]
    [InlineData("victory-lap-fallback", "Dedication feat. Kendrick Lamar - Nipsey Hussle, Victory Lap", "Nipsey Hussle", 246, "Dedication")]
    [InlineData("that-good-fallback", "OFFICIAL MUSIC VIDEO: Snoop Dogg f. Wiz Khalifa - That Good", "SnoopDoggTV", 250, "That Good")]
    public void Fallback_finds_the_song_but_only_suggests_it(string fixture, string rawTitle, string channel, int duration, string title)
    {
        var query = Video(rawTitle, duration, channel);
        var result = MbMatcher.Match(query, Fixture(fixture), fallback: true);
        Assert.Equal(MbDecision.Suggested, result.Decision);
        Assert.Equal(title, result.Best!.Pick.Title);
    }

    [Fact]
    public void Strict_query_searches_core_title_and_main_artist()
    {
        var query = Video("SchoolBoy Q - Collard Greens ft. Kendrick Lamar (Official Video)", 284);
        Assert.Equal("recording:\"Collard Greens\" AND artist:\"SchoolBoy Q\" AND status:official", query.StrictLucene);
        Assert.Equal(["Kendrick Lamar"], query.Guests);
    }

    [Fact]
    public void Duration_allows_longer_videos_more_than_shorter_files()
    {
        Assert.Equal(1, MbMatcher.DurationScore(200, 198_000));
        Assert.True(MbMatcher.DurationScore(230, 200_000) > 0.6);
        Assert.Equal(0, MbMatcher.DurationScore(180, 200_000));
        Assert.Equal(MbMatcher.UNKNOWN_DURATION_SCORE, MbMatcher.DurationScore(null, 200_000));
    }
}
