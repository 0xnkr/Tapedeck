using Tapedeck.Core.Models;
using Tapedeck.Core.MusicBrainz;

namespace Tapedeck.Core.Lyrics;

/// <summary>
/// Picks the lyrics that belong to a track. Wrong lyrics are worse than none: title and main artist have to fit,
/// and a live or remix version only counts when the track names it too. Timed lyrics are taken only when the
/// recording they were timed to is as long as the file; a music video with an intro gets the plain text.
/// </summary>
public static class LyricsMatcher
{
    /// <summary>LRCLIB's own tolerance for <c>/api/get</c>; a longer gap shifts every line.</summary>
    public const double SYNC_TOLERANCE_S = 2;

    private const double MIN_SIMILARITY = 0.9;

    /// <summary>The best fitting record as lyrics to store; <see cref="LrclibLyrics.NONE"/> when none fits.</summary>
    public static LrclibLyrics Pick(LyricsRequest request, IReadOnlyList<LrclibRecord> records)
    {
        var fitting = records.Where(r => Fits(request, r)).ToList();
        if (fitting.Count == 0)
            return LrclibLyrics.NONE;

        // records with lyrics first, then the closest length; most are uploads of the same song
        var best = fitting
            .OrderByDescending(r => r.Plain is not null || r.Synced is not null)
            .ThenBy(r => Gap(request, r) ?? double.MaxValue)
            .First();
        if (best.Instrumental && best.Plain is null && best.Synced is null)
            return new LrclibLyrics(null, null, Instrumental: true);

        var timed = TimingFits(request, fitting)
            ? fitting
                .Where(r => r.Synced is not null && (request.DurationS is null || Gap(request, r) <= SYNC_TOLERANCE_S))
                .OrderBy(r => Gap(request, r) ?? double.MaxValue)
                .FirstOrDefault()
            : null;
        var lrc = best.Synced ?? timed?.Synced;
        var plain = best.Plain ?? (lrc is null ? null : LrcText.ToPlain(lrc));
        return new LrclibLyrics(plain, timed?.Synced, Instrumental: false);
    }

    /// <summary>
    /// A YouTube Music upload is the release itself. Any other video only gets timed lyrics when it is as long as
    /// the song usually is: the length most records agree on. A music video can be as long as a live recording by
    /// chance, and that recording's timing would not fit.
    /// </summary>
    private static bool TimingFits(LyricsRequest request, IReadOnlyList<LrclibRecord> fitting)
    {
        if (request.IsTopic || request.DurationS is not { } file)
            return true;

        var lengths = fitting.Select(r => r.DurationS).OfType<double>().ToList();
        if (lengths.Count == 0)
            return false;
        var usual = lengths.MaxBy(length => lengths.Count(other => Math.Abs(other - length) <= SYNC_TOLERANCE_S));
        return Math.Abs(file - usual) <= SYNC_TOLERANCE_S;
    }

    private static bool Fits(LyricsRequest request, LrclibRecord record)
    {
        if (MbNormalizer.Similarity(MbNormalizer.Core(request.Title), MbNormalizer.Core(record.Title)) < MIN_SIMILARITY)
            return false;
        if (MbNormalizer.Variants(request.Title) != MbNormalizer.Variants(record.Title))
            return false;

        var main = MbNormalizer.SplitArtists(MbNormalizer.SplitFeaturing(request.Artist).Main).FirstOrDefault() ?? request.Artist;
        return MbNormalizer.SplitArtists(MbNormalizer.SplitFeaturing(record.Artist).Main)
            .Append(record.Artist)
            .Any(a => MbNormalizer.Similarity(main, a) >= MIN_SIMILARITY);
    }

    private static double? Gap(LyricsRequest request, LrclibRecord record) =>
        request.DurationS is { } file && record.DurationS is { } timed ? Math.Abs(file - timed) : null;
}
