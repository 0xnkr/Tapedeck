using Tapedeck.Core;

namespace Tapedeck.Tests;

public class FileNamingTests
{
    [Theory]
    [InlineData("AC/DC", "Back In Black", "AC - DC - Back In Black")]
    [InlineData("Artist", "What? *Why*", "Artist - What Why")]
    [InlineData("Artist", "Title: Part 2", "Artist - Title - Part 2")]
    [InlineData("Artist", "Say \"Hi\"...", "Artist - Say 'Hi'")]
    [InlineData("", "Only Title", "Only Title")]
    public void Removes_characters_invalid_on_fat32(string artist, string title, string expected)
    {
        Assert.Equal(expected, FileNaming.BaseName(artist, title));
    }

    [Fact]
    public void Truncates_long_names()
    {
        var name = FileNaming.BaseName("A", new string('x', 500));
        Assert.True(name.Length <= FileNaming.MAX_BASE_LENGTH);
    }

    [Fact]
    public void Appends_video_id_on_collision()
    {
        Assert.Equal("Artist - Title.opus",
            FileNaming.Choose("Artist", "Title", "dQw4w9WgXcQ", "opus", _ => false));
        Assert.Equal("Artist - Title [dQw4w9WgXcQ].opus",
            FileNaming.Choose("Artist", "Title", "dQw4w9WgXcQ", "opus", _ => true));
    }

    [Fact]
    public void Repeated_download_keeps_the_tracks_own_file()
    {
        // the existing file is taken by the track itself, which must not count as a collision
        Assert.Equal("Artist - Title.opus",
            FileNaming.ChooseForTrack("Artist - Title.opus", "Artist", "Title", "dQw4w9WgXcQ", "opus", _ => true));
        Assert.Equal("Artist - Title [dQw4w9WgXcQ].opus",
            FileNaming.ChooseForTrack("Artist - Title [dQw4w9WgXcQ].opus", "Artist", "Title", "dQw4w9WgXcQ", "opus", _ => true));
    }

    [Fact]
    public void First_download_or_other_format_chooses_a_new_name()
    {
        Assert.Equal("Artist - Title.opus",
            FileNaming.ChooseForTrack(null, "Artist", "Title", "dQw4w9WgXcQ", "opus", _ => false));
        Assert.Equal("Artist - Title.opus",
            FileNaming.ChooseForTrack("Artist - Title.m4a", "Artist", "Title", "dQw4w9WgXcQ", "opus", _ => false));
    }
}
