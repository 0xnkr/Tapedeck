namespace Tapedeck.Core.Audio;

/// <summary>
/// Measures how a song sounds from mono PCM at <see cref="SAMPLE_RATE"/>: timbre, energy, dynamics and tempo, each
/// as mean and spread over the song. Pure computation, so it is testable with generated signals.
/// <list type="bullet">
/// <item>Intro and outro are cut (10 % each, at most 20 s, only for songs over a minute), silent frames
/// (fades, pauses) do not count.</item>
/// <item>Timbre per 2048-sample frame (~93 ms) with a hop of 1024: enough points for a stable mean, short enough
/// for the flux to see changes.</item>
/// <item>Tempo from an onset curve with a hop of 256 (~86 per second) and its autocorrelation between 60 and
/// 200 BPM, with harmonic enhancement and a slight preference for tempos near 115; a calm song without a steady
/// beat gets the half tempo (<see cref="FeltTempo"/>).</item>
/// </list>
/// Tested on 98 songs of the library with known tempo: 82 exact, 11 half or double, 5 at 2:3 or 3:4 (version 1:
/// 74, 15 and 9; ten ballads were measured at double tempo, now three).
/// </summary>
public static class AudioAnalysis
{
    /// <summary>
    /// Version of the measurement: a higher one measures every file again. Independent of
    /// <see cref="AudioFeatures.FORMAT_VERSION"/>, which only changes with the format of the tag.
    /// </summary>
    public const int VERSION = 2;

    public const int SAMPLE_RATE = 22050;

    private const int FRAME = 2048;
    private const int HOP = 1024;
    private const int ONSET_FRAME = 1024;
    private const int ONSET_HOP = 256;

    /// <summary>Frames quieter than this are fades or pauses and do not count.</summary>
    private const double SILENCE_DB = -50;

    private const double TRIM_SHARE = 0.10;
    private const double TRIM_MAX_S = 20;
    private const double TRIM_MIN_LENGTH_S = 60;

    /// <summary>Less sound than this says nothing about a song.</summary>
    private const double MIN_SOUND_S = 3;

    private const double ROLLOFF_SHARE = 0.85;
    private const double MIN_BPM = 60;
    private const double MAX_BPM = 200;
    private const double PREFERRED_BPM = 115;

    /// <summary>Spread of the tempo preference in octaves: 58 or 230 BPM need a clearly stronger beat than 115.</summary>
    private const double PREFERENCE_OCTAVES = 1.0;

    /// <summary>
    /// Harmonic enhancement: a tempo also counts the repetition two and four beats later with this weight. The true
    /// beat repeats there too, a triplet or off-beat pattern (2:3, 3:4 of the beat) does not.
    /// </summary>
    private const double HARMONIC_WEIGHT = 0.5;

    /// <summary>
    /// Ballads with steady eighth notes are measured at double tempo. A song at least this fast that sounds calm
    /// (<see cref="CALM_FLUX"/>) and has no steady beat (<see cref="STEADY_BEAT_CONFIDENCE"/>) gets the half tempo.
    /// </summary>
    private const double HALF_TIME_MIN_BPM = 120;

    /// <summary>Below this mean flux the arrangement is calm: ballads 0.26–0.34, driving songs 0.40–0.50.</summary>
    private const double CALM_FLUX = 0.35;

    /// <summary>A beat this clear is dance music, even when calm (A Sky Full of Stars, 0.65).</summary>
    private const double STEADY_BEAT_CONFIDENCE = 0.65;

    // ln(1 + c·m) compresses the spectrum, so quiet hi-hats count next to loud kicks
    private const double ONSET_COMPRESSION = 10;

    private const double EPSILON = 1e-12;

    /// <summary>The features of a song; <c>null</c> when it has less than a few seconds of sound.</summary>
    public static AudioFeatures? Analyze(ReadOnlySpan<float> samples)
    {
        var song = Trim(samples);
        var timbre = MeasureTimbre(song);
        if (timbre.Rms.Count * (double)HOP / SAMPLE_RATE < MIN_SOUND_S)
            return null;
        var (bpm, confidence) = MeasureTempo(song);
        var flux = Stat(timbre.Flux);
        return new AudioFeatures(
            FeltTempo(bpm, confidence, flux.Mean),
            confidence,
            Stat(timbre.Centroid),
            Stat(timbre.Flatness),
            flux,
            Stat(timbre.Rolloff),
            Stat(timbre.Zcr),
            Stat(timbre.Rms),
            Stat(MeasureCrest(song)));
    }

    /// <summary>
    /// The tempo as it is felt: the half of a fast tempo for a calm song without a steady beat, whose eighth notes
    /// were taken for the beat (Imagine 152 → 76, Wish You Were Here 122 → 61). Driving songs and dance music keep
    /// theirs.
    /// </summary>
    public static double FeltTempo(double bpm, double confidence, double fluxMean) =>
        bpm >= HALF_TIME_MIN_BPM && fluxMean < CALM_FLUX && confidence < STEADY_BEAT_CONFIDENCE ? bpm / 2 : bpm;

    /// <summary>Verse and chorus: without 10 % at each end (at most 20 s), only for songs over a minute.</summary>
    private static ReadOnlySpan<float> Trim(ReadOnlySpan<float> samples)
    {
        var seconds = samples.Length / (double)SAMPLE_RATE;
        if (seconds <= TRIM_MIN_LENGTH_S)
            return samples;
        var cut = (int)(Math.Min(seconds * TRIM_SHARE, TRIM_MAX_S) * SAMPLE_RATE);
        return samples[cut..^cut];
    }

    private sealed class Timbre
    {
        public readonly List<double> Centroid = [];
        public readonly List<double> Flatness = [];
        public readonly List<double> Flux = [];
        public readonly List<double> Rolloff = [];
        public readonly List<double> Zcr = [];
        public readonly List<double> Rms = [];
    }

    private static Timbre MeasureTimbre(ReadOnlySpan<float> song)
    {
        var result = new Timbre();
        var fft = new Fft(FRAME);
        var magnitudes = new double[fft.Bins];
        var current = new double[fft.Bins];
        var previous = new double[fft.Bins];
        var hasPrevious = false;
        const double BIN_HZ = (double)SAMPLE_RATE / FRAME;

        for (var offset = 0; offset + FRAME <= song.Length; offset += HOP)
        {
            var frame = song.Slice(offset, FRAME);
            var level = Decibels(Rms(frame));
            if (level < SILENCE_DB)
            {
                // the flux after a pause would only measure the pause
                hasPrevious = false;
                continue;
            }
            result.Rms.Add(level);
            result.Zcr.Add(ZeroCrossings(frame));

            fft.Magnitudes(song, offset, magnitudes);
            // bin 0 is DC, it says nothing about the sound
            double sum = 0, weighted = 0, power = 0, logPower = 0;
            for (var k = 1; k < magnitudes.Length; k++)
            {
                var m = magnitudes[k];
                var p = m * m;
                sum += m;
                weighted += m * k * BIN_HZ;
                power += p;
                logPower += Math.Log(p + EPSILON);
            }
            var bins = magnitudes.Length - 1;
            result.Centroid.Add(sum > 0 ? weighted / sum : 0);
            result.Flatness.Add(Math.Exp(logPower / bins) / (power / bins + EPSILON));

            var cumulative = 0.0;
            var rolloff = (magnitudes.Length - 1) * BIN_HZ;
            for (var k = 1; k < magnitudes.Length; k++)
            {
                cumulative += magnitudes[k] * magnitudes[k];
                if (cumulative >= ROLLOFF_SHARE * power)
                {
                    rolloff = k * BIN_HZ;
                    break;
                }
            }
            result.Rolloff.Add(rolloff);

            // flux of the normalized spectra: a change of the sound, not of the level
            var norm = Math.Sqrt(power) + EPSILON;
            var flux = 0.0;
            for (var k = 1; k < magnitudes.Length; k++)
            {
                current[k] = magnitudes[k] / norm;
                var rise = current[k] - previous[k];
                if (rise > 0)
                    flux += rise * rise;
            }
            if (hasPrevious)
                result.Flux.Add(Math.Sqrt(flux));
            (previous, current) = (current, previous);
            hasPrevious = true;
        }
        return result;
    }

    /// <summary>Peak to RMS in dB per second; heavily compressed tracks have little, acoustic ones a lot.</summary>
    private static List<double> MeasureCrest(ReadOnlySpan<float> song)
    {
        var result = new List<double>();
        for (var offset = 0; offset + SAMPLE_RATE <= song.Length; offset += SAMPLE_RATE)
        {
            var block = song.Slice(offset, SAMPLE_RATE);
            var rms = Rms(block);
            if (Decibels(rms) < SILENCE_DB)
                continue;
            var peak = 0.0;
            foreach (var sample in block)
                peak = Math.Max(peak, Math.Abs(sample));
            result.Add(20 * Math.Log10(peak / rms));
        }
        return result;
    }

    /// <summary>
    /// The tempo with the strongest repetition of the onset curve, and how strong it is (0–1). Without enough
    /// sound for a few beats: 0 and 0.
    /// </summary>
    private static (double Bpm, double Confidence) MeasureTempo(ReadOnlySpan<float> song)
    {
        const double FRAMES_PER_SECOND = (double)SAMPLE_RATE / ONSET_HOP;
        var envelope = OnsetEnvelope(song);
        var minLag = (int)Math.Floor(60 * FRAMES_PER_SECOND / MAX_BPM);
        var maxLag = (int)Math.Ceiling(60 * FRAMES_PER_SECOND / MIN_BPM);
        if (envelope.Length < 2 * maxLag)
            return (0, 0);

        // a few periods further, so the refinement below can average over them
        var acfLength = Math.Min(4 * maxLag + 8, envelope.Length / 2);
        var acf = Autocorrelation(envelope, acfLength);
        if (acf[0] <= 0)
            return (0, 0);

        // the strongest value at a lag ±1, so a period between two frames still counts
        double Near(int lag) => lag + 1 < acf.Length ? Math.Max(acf[lag], Math.Max(acf[lag - 1], acf[lag + 1])) : 0;

        var bestLag = 0;
        var bestScore = double.NegativeInfinity;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            var bpm = 60 * FRAMES_PER_SECOND / lag;
            if (bpm is < MIN_BPM or > MAX_BPM)
                continue;
            var octaves = Math.Log2(bpm / PREFERRED_BPM) / PREFERENCE_OCTAVES;
            var enhanced = acf[lag] + HARMONIC_WEIGHT * (Near(2 * lag) + Near(4 * lag));
            var score = enhanced * Math.Exp(-0.5 * octaves * octaves);
            if (score > bestScore)
            {
                bestScore = score;
                bestLag = lag;
            }
        }
        if (bestLag == 0)
            return (0, 0);

        // the peak k periods away pins the period down to 1/k of a frame
        var period = (double)bestLag;
        for (var k = 4; k >= 1; k--)
        {
            var center = k * bestLag;
            if (center + k + 1 >= acf.Length)
                continue;
            var peak = center;
            for (var lag = Math.Max(1, center - k); lag <= center + k; lag++)
            {
                if (acf[lag] > acf[peak])
                    peak = lag;
            }
            period = Interpolate(acf, peak) / k;
            break;
        }

        var tempo = Math.Clamp(60 * FRAMES_PER_SECOND / period, MIN_BPM, MAX_BPM);
        return (tempo, Math.Clamp(acf[bestLag] / acf[0], 0, 1));
    }

    /// <summary>Rise of the compressed spectrum per frame, without its slow trend and lightly smoothed.</summary>
    private static double[] OnsetEnvelope(ReadOnlySpan<float> song)
    {
        var fft = new Fft(ONSET_FRAME);
        var magnitudes = new double[fft.Bins];
        var current = new double[fft.Bins];
        var previous = new double[fft.Bins];
        var raw = new List<double>();
        for (var offset = 0; offset + ONSET_FRAME <= song.Length; offset += ONSET_HOP)
        {
            fft.Magnitudes(song, offset, magnitudes);
            var rise = 0.0;
            for (var k = 1; k < magnitudes.Length; k++)
            {
                current[k] = Math.Log(1 + ONSET_COMPRESSION * magnitudes[k]);
                if (raw.Count > 0 && current[k] > previous[k])
                    rise += current[k] - previous[k];
            }
            raw.Add(rise);
            (previous, current) = (current, previous);
        }

        // minus the mean of the surrounding half second, only what sticks out stays
        var count = raw.Count;
        var prefix = new double[count + 1];
        for (var i = 0; i < count; i++)
            prefix[i + 1] = prefix[i] + raw[i];
        const int HALF_WINDOW = SAMPLE_RATE / ONSET_HOP / 4;
        var detrended = new double[count];
        for (var i = 0; i < count; i++)
        {
            var from = Math.Max(0, i - HALF_WINDOW);
            var to = Math.Min(count, i + HALF_WINDOW + 1);
            var local = (prefix[to] - prefix[from]) / (to - from);
            detrended[i] = Math.Max(0, raw[i] - local);
        }

        var smoothed = new double[count];
        for (var i = 0; i < count; i++)
        {
            var left = i > 0 ? detrended[i - 1] : 0;
            var right = i + 1 < count ? detrended[i + 1] : 0;
            smoothed[i] = 0.25 * left + 0.5 * detrended[i] + 0.25 * right;
        }
        return smoothed;
    }

    /// <summary>Autocorrelation per overlapping pair, so long lags are not weaker just for having fewer pairs.</summary>
    private static double[] Autocorrelation(double[] values, int length)
    {
        var result = new double[length];
        for (var lag = 0; lag < length; lag++)
        {
            var sum = 0.0;
            for (var i = 0; i + lag < values.Length; i++)
                sum += values[i] * values[i + lag];
            result[lag] = sum / (values.Length - lag);
        }
        return result;
    }

    /// <summary>The top of a parabola through the peak and its neighbors.</summary>
    private static double Interpolate(double[] values, int peak)
    {
        if (peak <= 0 || peak + 1 >= values.Length)
            return peak;
        var left = values[peak - 1];
        var middle = values[peak];
        var right = values[peak + 1];
        var curve = left - 2 * middle + right;
        return curve >= 0 ? peak : peak + 0.5 * (left - right) / curve;
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        var sum = 0.0;
        foreach (var sample in samples)
            sum += sample * (double)sample;
        return Math.Sqrt(sum / samples.Length);
    }

    private static double Decibels(double rms) => 20 * Math.Log10(Math.Max(rms, 1e-10));

    private static double ZeroCrossings(ReadOnlySpan<float> frame)
    {
        var crossings = 0;
        for (var i = 1; i < frame.Length; i++)
        {
            if (frame[i] >= 0 != frame[i - 1] >= 0)
                crossings++;
        }
        return crossings / (double)(frame.Length - 1);
    }

    private static FeatureStat Stat(List<double> values)
    {
        if (values.Count == 0)
            return new FeatureStat(0, 0);
        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
        return new FeatureStat(mean, Math.Sqrt(variance));
    }
}
