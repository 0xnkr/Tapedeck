using Tapedeck.Core.Services;

namespace Tapedeck.Tests;

/// <summary>The TagLib# bug with comment packets of a multiple of 255 bytes, and its repair.</summary>
public sealed class OggCommentRepairTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), $"tapedeck-ogg-{Guid.NewGuid():N}");
    private readonly string path;

    public OggCommentRepairTests()
    {
        Directory.CreateDirectory(this.dir);
        this.path = Path.Combine(this.dir, "song.opus");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Audio", "tone-2s.opus"), this.path);
    }

    public void Dispose() => Directory.Delete(this.dir, recursive: true);

    /// <summary>Lengthens the title until TagLib# writes a file it cannot open again; returns that title.</summary>
    private string BreakWithTagLib()
    {
        for (var length = 1; length <= 300; length++)
        {
            var title = new string('a', length);
            using (var file = TagLib.File.Create(this.path))
            {
                file.Tag.Title = title;
                file.Save();
            }
            try
            {
                using var reopened = TagLib.File.Create(this.path);
            }
            catch (ArgumentNullException)
            {
                return title;
            }
        }
        throw new InvalidOperationException("TagLib# wrote no broken file; the workaround may no longer be needed.");
    }

    [Fact]
    public void Adds_the_missing_end_so_the_file_opens_again_with_its_tags()
    {
        var title = this.BreakWithTagLib();

        Assert.True(OggCommentRepair.Repair(this.path));

        using var file = TagLib.File.Create(this.path);
        Assert.Equal(title, file.Tag.Title);
        Assert.True(file.Properties.Duration > TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Leaves_a_sound_file_alone()
    {
        var before = File.ReadAllBytes(this.path);

        Assert.False(OggCommentRepair.Repair(this.path));
        Assert.Equal(before, File.ReadAllBytes(this.path));
    }
}
