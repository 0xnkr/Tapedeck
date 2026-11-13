using System.Globalization;

namespace Tapedeck.Core.Audio;

/// <summary>Mean and standard deviation of a value over the frames of a song.</summary>
public readonly record struct FeatureStat(double Mean, double Std);

/// <summary>
/// How a song sounds, measured by <see cref="AudioAnalysis"/> and written to the file as <c>TAPEDECK_AUDIO</c>
/// (<see cref="Recommendations.RecommendationTags.AUDIO_FIELD"/>). Raw physical values: a player compares them
/// relative to its own library, so the format stays stable when its weighting changes.
/// </summary>
/// <param name="Bpm">Tempo in beats per minute, 60–200; half or double time of the felt tempo is possible.</param>
/// <param name="BpmConfidence">0–1: how clearly the beat repeats (normalized autocorrelation at the tempo).</param>
/// <param name="Centroid">Spectral centroid in Hz: brightness.</param>
/// <param name="Flatness">Spectral flatness 0–1: noise and distortion (high) vs. clear tones (low).</param>
/// <param name="Flux">Spectral flux 0–~1.4 between frames: how busy the arrangement is.</param>
/// <param name="Rolloff">Frequency in Hz below which 85 % of the energy lies.</param>
/// <param name="Zcr">Zero-crossing rate per sample.</param>
/// <param name="Rms">Frame level in dBFS: energy (includes the mastering).</param>
/// <param name="Crest">Peak to RMS per second in dB: dynamics, low for heavily compressed tracks.</param>
public sealed record AudioFeatures(
    double Bpm,
    double BpmConfidence,
    FeatureStat Centroid,
    FeatureStat Flatness,
    FeatureStat Flux,
    FeatureStat Rolloff,
    FeatureStat Zcr,
    FeatureStat Rms,
    FeatureStat Crest)
{
    /// <summary>
    /// Version of the format (<c>v=</c> in the tag), the contract with the phone: which values there are and how
    /// they are written. The phone ignores versions it does not know. A better measurement with the same values
    /// raises <see cref="AudioAnalysis.VERSION"/> instead.
    /// </summary>
    public const int FORMAT_VERSION = 1;

    private static readonly CultureInfo INVARIANT = CultureInfo.InvariantCulture;

    /// <summary>
    /// <c>v=1;bpm=123.4;bpmconf=0.62;centroid=2140/610;flatness=0.081/0.034;flux=0.112/0.051;rolloff=4810/1320;
    /// zcr=0.061/0.022;rms=-14.2/3.1;crest=11.8/2.0</c>, mean before the slash, standard deviation after it.
    /// </summary>
    public string Format() =>
        string.Join(';',
            $"v={FORMAT_VERSION}",
            $"bpm={this.Bpm.ToString("0.0", INVARIANT)}",
            $"bpmconf={this.BpmConfidence.ToString("0.00", INVARIANT)}",
            $"centroid={Pair(this.Centroid, "0")}",
            $"flatness={Pair(this.Flatness, "0.000")}",
            $"flux={Pair(this.Flux, "0.000")}",
            $"rolloff={Pair(this.Rolloff, "0")}",
            $"zcr={Pair(this.Zcr, "0.0000")}",
            $"rms={Pair(this.Rms, "0.0")}",
            $"crest={Pair(this.Crest, "0.0")}");

    /// <summary>The values of <see cref="Format"/>; <c>null</c> for another version or a damaged text.</summary>
    public static AudioFeatures? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator > 0)
                values[entry[..separator]] = entry[(separator + 1)..];
        }
        if (!values.TryGetValue("v", out var version) || version != FORMAT_VERSION.ToString(INVARIANT))
            return null;

        try
        {
            return new AudioFeatures(
                Number(values["bpm"]),
                Number(values["bpmconf"]),
                ParsePair(values["centroid"]),
                ParsePair(values["flatness"]),
                ParsePair(values["flux"]),
                ParsePair(values["rolloff"]),
                ParsePair(values["zcr"]),
                ParsePair(values["rms"]),
                ParsePair(values["crest"]));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static string Pair(FeatureStat stat, string format) =>
        $"{Round(stat.Mean, format).ToString(format, INVARIANT)}/{Round(stat.Std, format).ToString(format, INVARIANT)}";

    // "-0" would look odd in the file
    private static double Round(double value, string format)
    {
        var rounded = double.Parse(value.ToString(format, INVARIANT), INVARIANT);
        return rounded == 0 ? 0 : rounded;
    }

    private static FeatureStat ParsePair(string text)
    {
        var parts = text.Split('/');
        if (parts.Length != 2)
            throw new FormatException($"Expected mean/std, got \"{text}\".");
        return new FeatureStat(Number(parts[0]), Number(parts[1]));
    }

    private static double Number(string text) => double.Parse(text, NumberStyles.Float, INVARIANT);
}
