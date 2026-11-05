using Tapedeck.Core.MusicBrainz;

namespace Tapedeck.Tests;

public class MbNormalizerTests
{
    [Theory]
    [InlineData("Gangsta’s Paradise", "Gangsta's Paradise")]
    [InlineData("Bitch, Don’t Kill My Vibe", "Bitch Don't Kill My Vibe")]
    [InlineData("Ty Dolla $ign", "Ty Dolla Sign")]
    [InlineData("HUMBLE.", "Humble")]
    [InlineData("ScHoolboy Q", "SchoolBoy Q")]
    [InlineData("Simon & Garfunkel", "Simon and Garfunkel")]
    [InlineData("The Coup De Grâce", "Coup de Grace")]
    public void Normalizes_to_equal_text(string a, string b) =>
        Assert.Equal(MbNormalizer.Normalize(a), MbNormalizer.Normalize(b));

    [Theory]
    [InlineData("Collard Greens ft. Kendrick Lamar", "Collard Greens", new[] { "Kendrick Lamar" })]
    [InlineData("Blindfolds (feat. Juicy J)", "Blindfolds", new[] { "Juicy J" })]
    [InlineData("Medicated feat. Chevy Woods & Juicy J", "Medicated", new[] { "Chevy Woods", "Juicy J" })]
    [InlineData("OFFICIAL MUSIC VIDEO: Snoop Dogg f. Wiz Khalifa", "OFFICIAL MUSIC VIDEO: Snoop Dogg", new[] { "Wiz Khalifa" })]
    [InlineData("Lord Knows ft. Dom Kennedy & Rick Ross (Sign Language)", "Lord Knows (Sign Language)", new[] { "Dom Kennedy", "Rick Ross" })]
    [InlineData("P.I.M.P. (Snoop Dogg Remix)", "P.I.M.P. (Snoop Dogg Remix)", new string[0])]
    public void Splits_guests(string text, string main, string[] guests)
    {
        var (actualMain, actualGuests) = MbNormalizer.SplitFeaturing(text);
        Assert.Equal(main, actualMain);
        Assert.Equal(guests, actualGuests);
    }

    [Theory]
    [InlineData("Venom (Music From The Motion Picture)", "Venom")]
    [InlineData("All The Stars (From \"Black Panther: The Album\")", "All The Stars")]
    [InlineData("Money Trees - Live at Melkweg", "Money Trees")]
    [InlineData("Unclosed Bracket)", "Unclosed Bracket")]
    [InlineData("A.D.H.D", "A.D.H.D")]
    public void Core_drops_qualifiers(string title, string core) => Assert.Equal(core, MbNormalizer.Core(title));

    [Theory]
    [InlineData("Money Trees (Live From Paris)", MbVariant.Live)]
    [InlineData("P.I.M.P. (Snoop Dogg Remix)", MbVariant.Remix)]
    [InlineData("Lord Knows (Sign Language)", MbVariant.SignLanguage)]
    [InlineData("Alright (Chopped Not Slopped by Slim K)", MbVariant.Slowed)]
    [InlineData("money trees (sped up version)", MbVariant.SpedUp)]
    [InlineData("Money Trees (Explicit Version)", MbVariant.None)]
    [InlineData("Live Your Life (feat. Rihanna)", MbVariant.None)]
    [InlineData("Venom (Music From The Motion Picture)", MbVariant.None)]
    public void Detects_variants_in_qualifiers(string title, MbVariant expected) =>
        Assert.Equal(expected, MbNormalizer.Variants(MbNormalizer.Qualifiers(title)));

    [Theory]
    [InlineData("good kid, m.A.A.d city", "good kid, m.A.A.d city (Deluxe)")]
    [InlineData("DAMN.", "DAMN. COLLECTORS EDITION.")]
    public void Edition_ignores_deluxe_and_collectors(string a, string b) =>
        Assert.Equal(MbNormalizer.Edition(a), MbNormalizer.Edition(b));

    [Fact]
    public void Similarity_is_one_for_equal_and_lower_for_different()
    {
        Assert.Equal(1, MbNormalizer.Similarity("Stayin' Out All Night", "Stayin Out All Night"));
        Assert.True(MbNormalizer.Similarity("Swimming Pools", "Swimming Pools (Drank)") >= 0.6);
        Assert.True(MbNormalizer.Similarity("Alright", "Money Trees") < 0.3);
    }

    [Fact]
    public void Search_words_drop_noise_and_punctuation()
    {
        var words = MbNormalizer.SearchWords("OFFICIAL MUSIC VIDEO: Snoop Dogg f. Wiz Khalifa - That Good");
        Assert.Equal(["snoop", "dogg", "wiz", "khalifa", "that", "good"], words);
    }
}
