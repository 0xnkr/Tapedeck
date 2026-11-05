namespace Tapedeck.Core.MusicBrainz;

public enum MbDecision
{
    /// <summary>Certain enough to apply without asking.</summary>
    Matched,
    /// <summary>Plausible; the user confirms on the detail page.</summary>
    Suggested,
    NoMatch,
}

/// <summary>A recognized song with its partial scores, for the decision and for the candidate list.</summary>
public sealed record MbCandidate(
    MbPick Pick,
    double Score,
    double TitleScore,
    double ArtistScore,
    double DurationScore,
    double? AlbumScore);

public sealed record MbMatchResult(MbDecision Decision, IReadOnlyList<MbCandidate> Candidates)
{
    public static readonly MbMatchResult NONE = new(MbDecision.NoMatch, []);

    public MbCandidate? Best => this.Candidates.Count > 0 ? this.Candidates[0] : null;
}

/// <summary>
/// Decides whether a MusicBrainz search found the song of a YouTube video.
/// <list type="number">
/// <item>Recordings of another version (live, remix, …) than the video drop out.</item>
/// <item>The rest is grouped into songs (same core title and main artist) and scored:
/// title 0.40, artist 0.35, duration 0.15, album 0.10 (only when YouTube names one).</item>
/// <item>Title below 0.75 or artist below 0.6 rules a song out.</item>
/// <item>Matched from 0.90 with a lead of 0.10 over the next other song, suggested from 0.65.</item>
/// </list>
/// The release of the winning song is chosen by <see cref="ReleasePicker"/>.
/// </summary>
public static class MbMatcher
{
    public const double TITLE_WEIGHT = 0.40;
    public const double ARTIST_WEIGHT = 0.35;
    public const double DURATION_WEIGHT = 0.15;
    public const double ALBUM_WEIGHT = 0.10;

    public const double MIN_TITLE = 0.75;
    public const double MIN_ARTIST = 0.6;

    public const double MATCH_SCORE = 0.90;
    public const double MATCH_LEAD = 0.10;
    public const double SUGGEST_SCORE = 0.65;

    /// <summary>A guest or a co-artist instead of the main artist.</summary>
    public const double CO_ARTIST_SCORE = 0.85;

    /// <summary>Unknown duration: neither for nor against the song.</summary>
    public const double UNKNOWN_DURATION_SCORE = 0.5;

    /// <summary>Up to this difference in seconds the duration counts as equal.</summary>
    public const double DURATION_TOLERANCE_S = 3;

    /// <summary>A longer video (intro, outro, skit) scores 0 from this many extra seconds.</summary>
    public const double LONGER_LIMIT_S = 90;

    /// <summary>A shorter file is a cut or snippet; it scores 0 from this many missing seconds.</summary>
    public const double SHORTER_LIMIT_S = 20;

    public const int MAX_CANDIDATES = 3;

    /// <param name="fallback">
    /// The recordings come from the fallback search over the raw title; its result is never more than a suggestion.
    /// </param>
    public static MbMatchResult Match(MbQuery query, IReadOnlyList<MbRecording> recordings, bool fallback)
    {
        var candidates = recordings
            .Where(r => r.MainArtist is not null && Variants(r) == query.Variants)
            .GroupBy(r => (Title: MbNormalizer.Normalize(MbNormalizer.Core(r.Title)), Artist: r.MainArtist!.ArtistId))
            .Select(g => Score(query, g.ToList(), fallback))
            .OfType<MbCandidate>()
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Pick.Date ?? "9999", StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
            return MbMatchResult.NONE;

        var best = candidates[0];
        // other recordings of the same song credited differently ("SZA & Kendrick Lamar") are no rival
        var bestTitle = MbNormalizer.Normalize(MbNormalizer.Core(best.Pick.Title));
        var rival = candidates.Skip(1).FirstOrDefault(c => MbNormalizer.Normalize(MbNormalizer.Core(c.Pick.Title)) != bestTitle);
        var lead = best.Score - (rival?.Score ?? 0);

        // scores are rounded to three places, so a lead of exactly 0.1 may come out a hair below
        var decision = !fallback && best.Score >= MATCH_SCORE && lead >= MATCH_LEAD - 0.0005 ? MbDecision.Matched
            : best.Score >= SUGGEST_SCORE ? MbDecision.Suggested
            : MbDecision.NoMatch;
        return new MbMatchResult(decision, candidates.Take(MAX_CANDIDATES).ToList());
    }

    /// <summary>Versions a recording is: from its title qualifiers, its disambiguation and its releases.</summary>
    public static MbVariant Variants(MbRecording recording)
    {
        var variants = MbNormalizer.Variants(MbNormalizer.Qualifiers(recording.Title) + " " + recording.Disambiguation);

        // only on live albums or DJ mixes, even without saying so in the title
        if (recording.Releases.Count > 0)
        {
            if (recording.Releases.All(r => HasSecondary(r, "Live")))
                variants |= MbVariant.Live;
            if (recording.Releases.All(r => HasSecondary(r, "DJ-mix")))
                variants |= MbVariant.DjMix;
        }

        return variants;
    }

    public static double DurationScore(int? fileSeconds, int? recordingMs)
    {
        if (fileSeconds is not { } file || recordingMs is not { } ms)
            return UNKNOWN_DURATION_SCORE;

        var difference = file - ms / 1000.0;
        if (Math.Abs(difference) <= DURATION_TOLERANCE_S)
            return 1;
        return difference > 0
            ? Math.Max(0, 1 - (difference - DURATION_TOLERANCE_S) / (LONGER_LIMIT_S - DURATION_TOLERANCE_S))
            : Math.Max(0, 1 - (-difference - DURATION_TOLERANCE_S) / (SHORTER_LIMIT_S - DURATION_TOLERANCE_S));
    }

    private static MbCandidate? Score(MbQuery query, IReadOnlyList<MbRecording> song, bool fallback)
    {
        // the recordings of a song can be credited differently, the best one counts
        var title = song.Max(r => TitleScore(query, r, fallback));
        var artist = song.Max(r => ArtistScore(query, r, fallback));
        if (title < MIN_TITLE || artist < MIN_ARTIST)
            return null;

        var duration = song.Max(r => DurationScore(query.DurationS, r.LengthMs));
        double? album = string.IsNullOrWhiteSpace(query.Album)
            ? null
            : song.SelectMany(r => r.Releases)
                .Select(rel => MbNormalizer.SimilarityNormalized(MbNormalizer.Edition(query.Album), MbNormalizer.Edition(rel.Title)))
                .DefaultIfEmpty(0)
                .Max();

        var weighted = TITLE_WEIGHT * title + ARTIST_WEIGHT * artist + DURATION_WEIGHT * duration + ALBUM_WEIGHT * (album ?? 0);
        var weights = TITLE_WEIGHT + ARTIST_WEIGHT + DURATION_WEIGHT + (album is null ? 0 : ALBUM_WEIGHT);
        var score = weighted / weights;

        return ReleasePicker.Pick(song, score) is { } pick
            ? new MbCandidate(pick, Math.Round(score, 3), title, artist, duration, album)
            : null;
    }

    private static double TitleScore(MbQuery query, MbRecording recording, bool fallback)
    {
        var recordingTitle = MbNormalizer.Normalize(MbNormalizer.Core(recording.Title));
        var similarity = MbNormalizer.SimilarityNormalized(MbNormalizer.Normalize(query.CoreTitle), recordingTitle);
        if (!fallback || query.RawTitle is null)
            return similarity;

        // the raw title without the artist's name, so a song called like its artist is not found in it
        var raw = MbNormalizer.Normalize(query.RawTitle);
        foreach (var credit in recording.Artists)
        {
            raw = MbNormalizer.RemoveWords(raw, MbNormalizer.Normalize(credit.Name));
            raw = MbNormalizer.RemoveWords(raw, MbNormalizer.Normalize(credit.ArtistName));
        }
        return MbNormalizer.ContainsWords(raw, recordingTitle) ? 1 : similarity;
    }

    private static double ArtistScore(MbQuery query, MbRecording recording, bool fallback)
    {
        var main = recording.MainArtist!;
        var mainNames = Names(main);
        var allNames = recording.Artists.SelectMany(Names).ToList();
        var artists = query.Artists.Select(MbNormalizer.Normalize).Where(a => a.Length > 0).ToList();
        var guests = query.Guests.Select(MbNormalizer.Normalize).Where(g => g.Length > 0).ToList();

        if (artists.Any(mainNames.Contains))
            return 1;

        if (fallback)
        {
            var raw = MbNormalizer.Normalize($"{query.RawTitle} {query.Channel}");
            if (mainNames.Any(n => MbNormalizer.ContainsWords(raw, n)))
                return 1;
        }

        // "Kendrick Lamar" uploads "Baby Keem & Kendrick Lamar", or the main artist is only named as a guest
        if (artists.Any(allNames.Contains) || guests.Any(mainNames.Contains))
            return CO_ARTIST_SCORE;

        return artists.Count == 0
            ? 0
            : artists.SelectMany(a => mainNames.Select(n => MbNormalizer.SimilarityNormalized(a, n))).Max();
    }

    private static IEnumerable<string> Names(MbArtistCredit credit) =>
        new[] { MbNormalizer.Normalize(credit.Name), MbNormalizer.Normalize(credit.ArtistName) }.Distinct();

    private static bool HasSecondary(MbRelease release, string type) =>
        release.ReleaseGroup.SecondaryTypes.Contains(type, StringComparer.OrdinalIgnoreCase);
}
