using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.Services;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>Reading ffmpeg's loudness summary, the Opus track gain, and storing the value per track.</summary>
public sealed class LoudnessTests : IAsyncLifetime
{
    // the end of ffmpeg 6.1's output for tone-2s.opus with -af ebur128=framelog=quiet
    private const string FFMPEG_SUMMARY = """
        size=N/A time=00:00:01.99 bitrate=N/A speed= 125x
        [Parsed_ebur128_0 @ 000001b6b86f8600] Summary:

          Integrated loudness:
            I:         -21.8 LUFS
            Threshold: -31.8 LUFS

          Loudness range:
            LRA:         0.0 LU
            Threshold:   0.0 LUFS
            LRA low:     0.0 LUFS
            LRA high:    0.0 LUFS
        """;

    private readonly string dir = Path.Combine(Path.GetTempPath(), $"tapedeck-loudness-{Guid.NewGuid():N}");
    private TrackRepository repo = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(this.dir);
        var factory = new SqliteConnectionFactory(Path.Combine(this.dir, "tapedeck.db"));
        await new Migrator(factory).MigrateAsync();
        this.repo = new TrackRepository(factory);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(this.dir, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public void Reads_the_integrated_loudness_from_the_summary() =>
        Assert.Equal(-21.8, LoudnessMeter.ParseIntegrated(FFMPEG_SUMMARY));

    [Fact]
    public void Output_without_summary_has_no_value() =>
        Assert.Null(LoudnessMeter.ParseIntegrated("Error opening input file song.opus."));

    [Theory]
    // a loud music video is turned down, a quiet upload up, in 1/256 dB to -23 LUFS
    [InlineData(-8.0, -3840)]
    [InlineData(-23.0, 0)]
    [InlineData(-21.8, -307)]
    [InlineData(-30.5, 1920)]
    // the tag holds a signed 16 bit number
    [InlineData(-200.0, 32767)]
    public void Track_gain_is_the_distance_to_the_reference(double lufs, int gain) =>
        Assert.Equal(gain, LoudnessMeter.TrackGain(lufs));

    [Theory]
    // a loud music video is played 10 dB quieter, the target level as it is
    [InlineData(-8.0, 0.316)]
    [InlineData(-18.0, 1.0)]
    // a quiet one is raised by at most 6 dB
    [InlineData(-30.0, 1.995)]
    public void Playback_gain_brings_tracks_to_the_target(double lufs, double factor) =>
        Assert.Equal(factor, LoudnessMeter.PlaybackGain(lufs), 3);

    [Fact]
    public void Without_measurement_or_for_silence_the_file_plays_as_it_is()
    {
        Assert.Equal(1f, LoudnessMeter.PlaybackGain(null));
        Assert.Equal(1f, LoudnessMeter.PlaybackGain(-70));
    }

    [Fact]
    public async Task Only_downloaded_tracks_without_value_are_listed_and_a_new_value_outdates_the_tags()
    {
        var done = await this.repo.AddAsync("dQw4w9WgXcQ", "dQw4w9WgXcQ");
        await this.repo.UpdateDownloadStateAsync(done, DownloadStatus.Done, "A - One.opus", "opus");
        var pending = await this.repo.AddAsync("9bZkp7q19f0", "9bZkp7q19f0");
        await this.repo.MarkTagsWrittenAsync(done);

        Assert.Equal([done], await this.repo.ListWithoutLoudnessAsync());
        Assert.DoesNotContain(pending, await this.repo.ListWithoutLoudnessAsync());

        await this.repo.SetLoudnessAsync(done, -9.5);

        Assert.Empty(await this.repo.ListWithoutLoudnessAsync());
        Assert.Equal(-9.5, await this.repo.GetLoudnessAsync(done));
        Assert.True((await this.repo.GetDisplayAsync(done))!.TagsStale);
    }

    [Fact]
    public async Task The_same_value_again_leaves_the_tags_current()
    {
        var id = await this.repo.AddAsync("dQw4w9WgXcQ", "dQw4w9WgXcQ");
        await this.repo.UpdateDownloadStateAsync(id, DownloadStatus.Done, "A - One.opus", "opus");
        await this.repo.SetLoudnessAsync(id, -9.5);
        await this.repo.MarkTagsWrittenAsync(id);

        await this.repo.SetLoudnessAsync(id, -9.5);

        Assert.False((await this.repo.GetDisplayAsync(id))!.TagsStale);
    }
}
