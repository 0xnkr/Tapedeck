using Microsoft.Data.Sqlite;
using Tapedeck.Core.Audio;
using Tapedeck.Core.Models;
using Tapedeck.Data;

namespace Tapedeck.Tests;

/// <summary>The sound analysis on generated signals, its text format, the comparison and storing it per track.</summary>
public sealed class AudioAnalysisTests : IAsyncLifetime
{
    private const int RATE = AudioAnalysis.SAMPLE_RATE;

    private readonly string dir = Path.Combine(Path.GetTempPath(), $"tapedeck-audio-{Guid.NewGuid():N}");
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

    private static float[] Sine(double seconds, double hz, double amplitude = 0.5) =>
        Enumerable.Range(0, (int)(seconds * RATE)).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / RATE))).ToArray();

    private static float[] Noise(double seconds, double amplitude = 0.3, int seed = 7)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, (int)(seconds * RATE)).Select(_ => (float)(amplitude * (2 * random.NextDouble() - 1))).ToArray();
    }

    private static float[] Silence(double seconds) => new float[(int)(seconds * RATE)];

    /// <summary>Short decaying 1 kHz bursts on every beat, like a metronome.</summary>
    private static float[] Clicks(double seconds, double bpm)
    {
        var samples = new float[(int)(seconds * RATE)];
        var period = 60.0 / bpm * RATE;
        for (var beat = 0.0; beat < samples.Length; beat += period)
        {
            var start = (int)Math.Round(beat);
            for (var i = 0; i < RATE / 50 && start + i < samples.Length; i++)
                samples[start + i] = (float)(0.8 * Math.Exp(-i / (RATE / 200.0)) * Math.Sin(2 * Math.PI * 1000 * i / RATE));
        }
        return samples;
    }

    [Fact]
    public void A_sine_is_tonal_and_its_brightness_is_its_frequency()
    {
        var features = AudioAnalysis.Analyze(Sine(10, 1000))!;

        Assert.InRange(features.Centroid.Mean, 950, 1050);
        Assert.InRange(features.Flatness.Mean, 0, 0.01);
        // a steady tone neither changes nor varies in level
        Assert.InRange(features.Flux.Mean, 0, 0.01);
        Assert.InRange(features.Rms.Std, 0, 0.1);
        // amplitude 0.5: RMS 0.354 = -9.0 dBFS, peak to RMS 3 dB
        Assert.InRange(features.Rms.Mean, -9.2, -8.8);
        Assert.InRange(features.Crest.Mean, 2.9, 3.1);
    }

    [Fact]
    public void Noise_is_flat_and_bright()
    {
        var noise = AudioAnalysis.Analyze(Noise(10))!;
        var sine = AudioAnalysis.Analyze(Sine(10, 440))!;

        Assert.True(noise.Flatness.Mean > 0.4, $"flatness {noise.Flatness.Mean}");
        Assert.True(noise.Centroid.Mean > 4000, $"centroid {noise.Centroid.Mean}");
        Assert.True(noise.Zcr.Mean > 10 * sine.Zcr.Mean);
        Assert.True(noise.Rolloff.Mean > 8000, $"rolloff {noise.Rolloff.Mean}");
    }

    [Theory]
    [InlineData(120)]
    [InlineData(90)]
    [InlineData(150)]
    [InlineData(72)]
    public void Finds_the_tempo_of_a_click_track(double bpm)
    {
        var features = AudioAnalysis.Analyze(Clicks(30, bpm))!;

        Assert.InRange(features.Bpm, bpm - 2, bpm + 2);
        Assert.True(features.BpmConfidence > 0.3, $"confidence {features.BpmConfidence}");
    }

    [Theory]
    // a ballad's eighth notes taken for the beat: Imagine, Wish You Were Here
    [InlineData(152, 0.44, 0.297, 76)]
    [InlineData(122.5, 0.52, 0.327, 61.25)]
    // a driving song keeps its tempo (Mr. Brightside), so does calm dance music with a steady beat (A Sky Full of Stars)
    [InlineData(148, 0.55, 0.425, 148)]
    [InlineData(125, 0.65, 0.335, 125)]
    // a slow tempo is never halved further
    [InlineData(80, 0.30, 0.300, 80)]
    public void A_calm_song_without_a_steady_beat_gets_the_half_tempo(double bpm, double confidence, double flux, double felt) =>
        Assert.Equal(felt, AudioAnalysis.FeltTempo(bpm, confidence, flux));

    [Fact]
    public void Silence_does_not_count()
    {
        var withPause = AudioAnalysis.Analyze([.. Silence(5), .. Sine(10, 1000)])!;

        Assert.InRange(withPause.Rms.Mean, -9.5, -8.8);
    }

    [Fact]
    public void Without_enough_sound_there_are_no_features()
    {
        Assert.Null(AudioAnalysis.Analyze(Silence(30)));
        Assert.Null(AudioAnalysis.Analyze(Sine(2, 1000)));
    }

    [Fact]
    public void Intro_and_outro_of_a_song_are_cut()
    {
        // 100 s: noise for the first and last 10 s, which is exactly what the trimming cuts
        var features = AudioAnalysis.Analyze([.. Noise(10), .. Sine(80, 1000), .. Noise(10)])!;

        Assert.InRange(features.Flatness.Mean, 0, 0.01);
        Assert.InRange(features.Centroid.Mean, 950, 1050);
    }

    [Fact]
    public void Format_and_parse_round_trip()
    {
        var features = new AudioFeatures(123.44, 0.623, new FeatureStat(2140.4, 610.2), new FeatureStat(0.0812, 0.0344),
            new FeatureStat(0.1121, 0.0513), new FeatureStat(4810, 1320), new FeatureStat(0.06123, 0.02234),
            new FeatureStat(-14.24, 3.12), new FeatureStat(11.84, 2.01));

        var text = features.Format();

        Assert.Equal("v=1;bpm=123.4;bpmconf=0.62;centroid=2140/610;flatness=0.081/0.034;flux=0.112/0.051;" +
                     "rolloff=4810/1320;zcr=0.0612/0.0223;rms=-14.2/3.1;crest=11.8/2.0", text);
        var parsed = AudioFeatures.Parse(text)!;
        Assert.Equal(123.4, parsed.Bpm);
        Assert.Equal(new FeatureStat(-14.2, 3.1), parsed.Rms);
        Assert.Equal(text, parsed.Format());
    }

    [Fact]
    public void Negative_zero_is_written_as_zero() =>
        Assert.Contains("rms=0.0/0.0", new AudioFeatures(0, 0, default, default, default, default, default,
            new FeatureStat(-0.01, 0), default).Format());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v=2;bpm=120.0")]
    [InlineData("v=1;bpm=120.0")]
    [InlineData("v=1;bpm=fast;bpmconf=0.50;centroid=1/1;flatness=1/1;flux=1/1;rolloff=1/1;zcr=1/1;rms=1/1;crest=1/1")]
    public void Unknown_versions_and_damaged_texts_are_ignored(string? text) =>
        Assert.Null(AudioFeatures.Parse(text));

    [Theory]
    [InlineData(120, 120, 1.0)]
    // half and double time count, a bit less
    [InlineData(120, 60, 0.85)]
    [InlineData(80, 160, 0.85)]
    public void Tempo_also_matches_as_half_or_double_time(double a, double b, double expected) =>
        Assert.Equal(expected, AudioSimilarity.TempoSimilarity(a, b), 3);

    [Fact]
    public void A_tempo_a_third_off_is_not_alike() =>
        Assert.True(AudioSimilarity.TempoSimilarity(120, 160) < 0.05);

    [Fact]
    public void Calm_songs_are_closer_to_each_other_than_to_a_loud_one()
    {
        var ballad = AudioAnalysis.Analyze([.. Sine(10, 440, 0.1), .. Sine(10, 330, 0.15)])!;
        var otherBallad = AudioAnalysis.Analyze([.. Sine(10, 494, 0.12), .. Sine(10, 370, 0.1)])!;
        var loud = AudioAnalysis.Analyze(Noise(20, 0.9))!;
        var similarity = AudioSimilarity.For([ballad, otherBallad, loud]);

        Assert.True(similarity.Similarity(ballad, otherBallad) > similarity.Similarity(ballad, loud));
        Assert.Equal(1.0, similarity.Similarity(loud, loud), 6);
    }

    [Fact]
    public async Task Tracks_are_measured_once_per_version_and_a_new_value_outdates_the_tags()
    {
        var done = await this.repo.AddAsync("dQw4w9WgXcQ", "dQw4w9WgXcQ");
        await this.repo.UpdateDownloadStateAsync(done, DownloadStatus.Done, "A - One.opus", "opus");
        var pending = await this.repo.AddAsync("9bZkp7q19f0", "9bZkp7q19f0");
        await this.repo.MarkTagsWrittenAsync(done);

        Assert.Equal([done], await this.repo.ListWithoutAudioFeaturesAsync(1));
        Assert.DoesNotContain(pending, await this.repo.ListWithoutAudioFeaturesAsync(1));

        // the timestamps have milliseconds; in the same one, the change would not count as newer
        await Task.Delay(5);
        await this.repo.SetAudioFeaturesAsync(done, "v=1;bpm=120.0", 1);

        Assert.Empty(await this.repo.ListWithoutAudioFeaturesAsync(1));
        Assert.Equal([done], await this.repo.ListWithoutAudioFeaturesAsync(2));
        Assert.Equal("v=1;bpm=120.0", await this.repo.GetAudioFeaturesAsync(done));
        Assert.True((await this.repo.GetDisplayAsync(done))!.TagsStale);
        Assert.Equal(done, Assert.Single(await this.repo.ListAudioFeaturesAsync()).Id);
    }

    [Fact]
    public async Task A_file_without_sound_is_not_measured_again_and_the_same_value_keeps_the_tags()
    {
        var id = await this.repo.AddAsync("dQw4w9WgXcQ", "dQw4w9WgXcQ");
        await this.repo.UpdateDownloadStateAsync(id, DownloadStatus.Done, "A - One.opus", "opus");
        await this.repo.SetAudioFeaturesAsync(id, null, 1);
        await this.repo.MarkTagsWrittenAsync(id);

        Assert.Empty(await this.repo.ListWithoutAudioFeaturesAsync(1));

        await this.repo.SetAudioFeaturesAsync(id, null, 1);

        Assert.False((await this.repo.GetDisplayAsync(id))!.TagsStale);
    }

    [Fact]
    public void The_report_lists_the_distribution_and_the_closest_songs()
    {
        var calm = AudioAnalysis.Analyze(Sine(10, 440, 0.1))!;
        var entries = new List<AudioReportEntry>
        {
            new(1, "A", "Calm", calm),
            new(2, "B", "Also | calm", AudioAnalysis.Analyze(Sine(10, 494, 0.12))!),
            new(3, "C", "Loud", AudioAnalysis.Analyze(Noise(10, 0.9))!),
        };

        var report = AudioReport.ToMarkdown(entries, 3);

        Assert.Contains("3 tracks with measured sound", report);
        Assert.Contains("| BPM |", report);
        Assert.Contains("### A – Calm", report);
        Assert.Contains("B – Also \\| calm", report);
    }
}
