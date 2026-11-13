namespace Tapedeck.Core.Audio;

/// <summary>
/// How alike two songs sound, 0–1, from their <see cref="AudioFeatures"/>. The same formula as the AUDIO block of
/// the phone player's recommendations; a change here must be made there too.
/// <list type="bullet">
/// <item>Every value is compared relative to the library: the difference divided by the robust spread
/// (1.4826 · median absolute deviation), then <c>exp(−d²/2)</c>. Hz, dB and ratios become comparable.</item>
/// <item>Weights: timbre 0.20 (centroid 0.11, flatness 0.09), energy and dynamics 0.16 (RMS 0.08, crest 0.08),
/// tempo 0.14, flux 0.06, rolloff and ZCR 0.04 (0.02 each); every value 70 % mean, 30 % spread. Rolloff and ZCR
/// stay low because they largely measure the same brightness as the centroid.</item>
/// <item>Tempo in octaves with a fixed spread of 0.1 (120 vs. 128 BPM ≈ 0.65), also as half or double time, which
/// counts 0.85. With a weak beat on either side the tempo counts half.</item>
/// </list>
/// </summary>
public sealed class AudioSimilarity
{
    public const double CENTROID_WEIGHT = 0.11;
    public const double FLATNESS_WEIGHT = 0.09;
    public const double RMS_WEIGHT = 0.08;
    public const double CREST_WEIGHT = 0.08;
    public const double BPM_WEIGHT = 0.14;
    public const double FLUX_WEIGHT = 0.06;
    public const double ROLLOFF_WEIGHT = 0.02;
    public const double ZCR_WEIGHT = 0.02;
    public const double MEAN_SHARE = 0.7;

    /// <summary>Tempo spread in octaves.</summary>
    public const double BPM_SPREAD_OCTAVES = 0.1;

    /// <summary>A match only as half or double time counts this much.</summary>
    public const double HALF_DOUBLE_FACTOR = 0.85;

    /// <summary>Below this confidence on either side, the tempo counts half.</summary>
    public const double WEAK_BEAT_CONFIDENCE = 0.3;

    private const double MAD_TO_SIGMA = 1.4826;

    private static readonly (Func<AudioFeatures, FeatureStat> Get, double Weight)[] VALUES =
    [
        (f => f.Centroid, CENTROID_WEIGHT),
        (f => f.Flatness, FLATNESS_WEIGHT),
        (f => f.Rms, RMS_WEIGHT),
        (f => f.Crest, CREST_WEIGHT),
        (f => f.Flux, FLUX_WEIGHT),
        (f => f.Rolloff, ROLLOFF_WEIGHT),
        (f => f.Zcr, ZCR_WEIGHT),
    ];

    // per value: spread of the means, spread of the standard deviations
    private readonly (double Mean, double Std)[] spreads;

    private AudioSimilarity((double Mean, double Std)[] spreads) => this.spreads = spreads;

    /// <summary>The comparison relative to the given library.</summary>
    public static AudioSimilarity For(IReadOnlyCollection<AudioFeatures> library) =>
        new(VALUES.Select(v => (Spread(library.Select(f => v.Get(f).Mean)), Spread(library.Select(f => v.Get(f).Std)))).ToArray());

    public double Similarity(AudioFeatures a, AudioFeatures b)
    {
        var sum = 0.0;
        var weights = 0.0;
        for (var i = 0; i < VALUES.Length; i++)
        {
            var (get, weight) = VALUES[i];
            var (va, vb) = (get(a), get(b));
            sum += weight * (MEAN_SHARE * Kernel(va.Mean - vb.Mean, this.spreads[i].Mean)
                             + (1 - MEAN_SHARE) * Kernel(va.Std - vb.Std, this.spreads[i].Std));
            weights += weight;
        }

        if (a.Bpm > 0 && b.Bpm > 0)
        {
            var weight = Math.Min(a.BpmConfidence, b.BpmConfidence) < WEAK_BEAT_CONFIDENCE ? BPM_WEIGHT / 2 : BPM_WEIGHT;
            sum += weight * TempoSimilarity(a.Bpm, b.Bpm);
            weights += weight;
        }
        return weights == 0 ? 0 : sum / weights;
    }

    /// <summary>Same tempo, or half/double time at <see cref="HALF_DOUBLE_FACTOR"/>, whichever is closer.</summary>
    public static double TempoSimilarity(double a, double b)
    {
        var same = Kernel(Math.Log2(a / b), BPM_SPREAD_OCTAVES);
        var half = Kernel(Math.Log2(a / b) - 1, BPM_SPREAD_OCTAVES);
        var twice = Kernel(Math.Log2(a / b) + 1, BPM_SPREAD_OCTAVES);
        return Math.Max(same, HALF_DOUBLE_FACTOR * Math.Max(half, twice));
    }

    private static double Kernel(double difference, double spread)
    {
        var d = difference / spread;
        return Math.Exp(-0.5 * d * d);
    }

    /// <summary>1.4826 · median absolute deviation; never 0, so a library of equal values compares as equal.</summary>
    private static double Spread(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
            return 1;
        var median = Median(sorted);
        var deviations = sorted.Select(v => Math.Abs(v - median)).OrderBy(v => v).ToArray();
        return Math.Max(MAD_TO_SIGMA * Median(deviations), 1e-9);
    }

    private static double Median(double[] sorted) =>
        sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
}
