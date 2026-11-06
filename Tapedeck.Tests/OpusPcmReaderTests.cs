using Tapedeck.Core.Playback;

namespace Tapedeck.Tests;

/// <summary>Decoding without an audio device: a 2-second 440 Hz tone and a file that is no Opus at all.</summary>
public class OpusPcmReaderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Audio", name);

    [Fact]
    public void Decodes_the_whole_file_as_48_khz_stereo()
    {
        using var reader = new OpusPcmReader(Fixture("tone-2s.opus"));
        Assert.InRange(reader.Duration.TotalSeconds, 1.9, 2.1);

        long samples = 0;
        while (reader.Read(OpusPcmReader.SAMPLE_RATE / 4 * OpusPcmReader.CHANNELS) is { } block)
            samples += block.Length;

        var seconds = (double)samples / OpusPcmReader.CHANNELS / OpusPcmReader.SAMPLE_RATE;
        Assert.InRange(seconds, 1.9, 2.1);
    }

    [Fact]
    public void Blocks_have_the_asked_size_until_the_end()
    {
        using var reader = new OpusPcmReader(Fixture("tone-2s.opus"));
        var block = reader.Read(12_000);
        Assert.NotNull(block);
        Assert.True(block.Length >= 12_000);
        Assert.True(block.Any(s => Math.Abs((int)s) > 1000), "a 440 Hz tone is not silence");
    }

    [Fact]
    public void Seeking_skips_ahead()
    {
        using var reader = new OpusPcmReader(Fixture("tone-2s.opus"));
        Assert.True(reader.CanSeek);
        reader.Seek(TimeSpan.FromSeconds(1.5));

        long samples = 0;
        while (reader.Read(4_800) is { } block)
            samples += block.Length;

        var seconds = (double)samples / OpusPcmReader.CHANNELS / OpusPcmReader.SAMPLE_RATE;
        Assert.InRange(seconds, 0.3, 0.7);
    }

    [Fact]
    public void A_broken_file_fails_with_a_clear_error()
    {
        var error = Record.Exception(() => new OpusPcmReader(Fixture("broken.opus")).Dispose());
        Assert.True(error is InvalidDataException or EndOfStreamException, error?.GetType().Name);
    }
}
