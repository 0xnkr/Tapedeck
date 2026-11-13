using System.Globalization;
using System.Text;

namespace Tapedeck.Core.Audio;

/// <summary>A track with measured sound, as the report lists it.</summary>
public sealed record AudioReportEntry(long Id, string Artist, string Title, AudioFeatures Features);

/// <summary>
/// The test bench for the sound analysis: how the values spread over the library, and for some songs their closest
/// neighbors by sound alone (<see cref="AudioSimilarity"/>, without genre, artist or tags). Shows whether ballads
/// find each other and the tempos are right, before a phone sees any of it.
/// </summary>
public static class AudioReport
{
    public const int NEIGHBORS = 10;

    private static readonly CultureInfo INVARIANT = CultureInfo.InvariantCulture;

    private static readonly (string Name, string Format, Func<AudioFeatures, double> Get)[] VALUES =
    [
        ("BPM", "0.0", f => f.Bpm),
        ("BPM confidence", "0.00", f => f.BpmConfidence),
        ("Centroid (Hz)", "0", f => f.Centroid.Mean),
        ("Centroid spread (Hz)", "0", f => f.Centroid.Std),
        ("Flatness", "0.000", f => f.Flatness.Mean),
        ("Flatness spread", "0.000", f => f.Flatness.Std),
        ("Flux", "0.000", f => f.Flux.Mean),
        ("Flux spread", "0.000", f => f.Flux.Std),
        ("Rolloff (Hz)", "0", f => f.Rolloff.Mean),
        ("Rolloff spread (Hz)", "0", f => f.Rolloff.Std),
        ("ZCR", "0.0000", f => f.Zcr.Mean),
        ("ZCR spread", "0.0000", f => f.Zcr.Std),
        ("RMS (dBFS)", "0.0", f => f.Rms.Mean),
        ("RMS spread (dB)", "0.0", f => f.Rms.Std),
        ("Crest (dB)", "0.0", f => f.Crest.Mean),
        ("Crest spread (dB)", "0.0", f => f.Crest.Std),
    ];

    /// <param name="entries">All tracks with measured sound.</param>
    /// <param name="count">How many songs get their neighbors listed.</param>
    /// <param name="seed">Picks the songs; the same seed gives the same report.</param>
    public static string ToMarkdown(IReadOnlyList<AudioReportEntry> entries, int count, int seed = 1)
    {
        var text = new StringBuilder();
        text.AppendLine("# Sound analysis");
        text.AppendLine();
        text.AppendLine(string.Create(INVARIANT, $"{entries.Count} tracks with measured sound (analysis version {AudioAnalysis.VERSION})."));
        if (entries.Count == 0)
            return text.ToString();

        text.AppendLine();
        text.AppendLine("## Distribution");
        text.AppendLine();
        text.AppendLine("| Value | 10 % | Median | 90 % |");
        text.AppendLine("|---|---:|---:|---:|");
        foreach (var (name, format, get) in VALUES)
        {
            var sorted = entries.Select(e => get(e.Features)).OrderBy(v => v).ToArray();
            text.AppendLine($"| {name} | {Quantile(sorted, 0.1).ToString(format, INVARIANT)} | " +
                            $"{Quantile(sorted, 0.5).ToString(format, INVARIANT)} | {Quantile(sorted, 0.9).ToString(format, INVARIANT)} |");
        }

        var similarity = AudioSimilarity.For(entries.Select(e => e.Features).ToList());
        var random = new Random(seed);
        var picked = entries.OrderBy(_ => random.Next()).Take(count).ToList();

        text.AppendLine();
        text.AppendLine("## Closest by sound");
        text.AppendLine();
        text.AppendLine("Sound alone, without genre, artist or tags; the phone adds those. Similarity 0–1.");
        foreach (var song in picked)
        {
            text.AppendLine();
            text.AppendLine($"### {Escape(song.Artist)} – {Escape(song.Title)}");
            text.AppendLine();
            text.AppendLine(Summary(song.Features));
            text.AppendLine();
            text.AppendLine("| # | Similarity | Song | BPM | RMS | Crest | Centroid |");
            text.AppendLine("|---:|---:|---|---:|---:|---:|---:|");
            var neighbors = entries
                .Where(e => e.Id != song.Id)
                .Select(e => (Entry: e, Value: similarity.Similarity(song.Features, e.Features)))
                .OrderByDescending(n => n.Value)
                .ThenBy(n => n.Entry.Id)
                .Take(NEIGHBORS);
            var place = 0;
            foreach (var (entry, value) in neighbors)
            {
                var f = entry.Features;
                text.AppendLine(string.Create(INVARIANT,
                    $"| {++place} | {value:0.00} | {Escape(entry.Artist)} – {Escape(entry.Title)} | {f.Bpm:0} | {f.Rms.Mean:0.0} | {f.Crest.Mean:0.0} | {f.Centroid.Mean:0} |"));
            }
        }
        return text.ToString();
    }

    private static string Summary(AudioFeatures f) =>
        string.Create(INVARIANT,
            $"`{f.Bpm:0} BPM ({f.BpmConfidence:0.00}) · RMS {f.Rms.Mean:0.0} ± {f.Rms.Std:0.0} dB · crest {f.Crest.Mean:0.0} dB · " +
            $"centroid {f.Centroid.Mean:0} Hz · flatness {f.Flatness.Mean:0.000} · flux {f.Flux.Mean:0.000}`");

    private static double Quantile(double[] sorted, double share)
    {
        var position = share * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    // a | in a title would split the table cell
    private static string Escape(string text) => text.Replace("|", "\\|");
}
