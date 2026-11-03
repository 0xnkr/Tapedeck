using Tapedeck.Core;

namespace Tapedeck.Tests;

public class TitleParserTests
{
    [Theory]
    [InlineData("Rick Astley - Never Gonna Give You Up (Official Music Video)", "Rick Astley", "Never Gonna Give You Up")]
    [InlineData("Daft Punk – Get Lucky [HD]", "Daft Punk", "Get Lucky")]
    [InlineData("Artist - Song (feat. Someone) (Lyrics)", "Artist", "Song (feat. Someone)")]
    [InlineData("Artist - Song (Remix) [Official Audio]", "Artist", "Song (Remix)")]
    [InlineData("Band - \"Quoted Title\" (Official Video) [4K]", "Band", "Quoted Title")]
    [InlineData("Some Title Without Artist (Audio)", null, "Some Title Without Artist")]
    public void Splits_and_cleans(string raw, string? artist, string title)
    {
        var parsed = TitleParser.Parse(raw);
        Assert.Equal(artist, parsed.Artist);
        Assert.Equal(title, parsed.Title);
    }
}
