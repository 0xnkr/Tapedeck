using System.Text.RegularExpressions;
using Tapedeck.Core.Models;
using Tapedeck.Core.MusicBrainz;

namespace Tapedeck.Core.Spotify;

/// <summary>How sure the YouTube search is about a song; <c>Accepted</c> and <c>Rejected</c> are the user's word.</summary>
public enum SpotifyMatchState
{
    /// <summary>A Topic upload of the same song and version with the expected length: taken without asking.</summary>
    Sure,
    /// <summary>Plausible candidates (a music video, another length), for the user to choose from.</summary>
    Unsure,
    NotFound,
    /// <summary>The user confirmed a video, or the song was imported.</summary>
    Accepted,
    /// <summary>The user said none of the candidates is the song.</summary>
    Rejected,
}

/// <summary>A search result judged against the song.</summary>
/// <param name="DurationFits"><c>null</c> when the length of the song or the video is unknown.</param>
/// <param name="OtherVersion">What makes the video another version than the song ("collins string", "other guests"); <c>null</c> when nothing does.</param>
public sealed record SpotifyCandidate(YtSearchResult Video, double TitleScore, double ArtistScore, bool? DurationFits, string? OtherVersion)
{
    /// <summary>Why the candidate is not sure, for the review list and the report.</summary>
    public IReadOnlyList<string> Problems(SpotifySong song)
    {
        var problems = new List<string>();
        if (!this.Video.IsTopic)
            problems.Add("no Topic upload");
        if (this.TitleScore < SpotifyYtMatcher.SURE_SCORE)
            problems.Add($"title {this.TitleScore:0.00}");
        if (this.ArtistScore < SpotifyYtMatcher.SURE_SCORE)
            problems.Add($"artist {this.ArtistScore:0.00}");
        if (this.DurationFits == false)
            problems.Add($"{this.Video.DurationS} s instead of {song.EstimatedDurationMs / 1000} s");
        if (this.OtherVersion is { } other)
            problems.Add($"version: {other}");
        return problems;
    }
}

/// <param name="Best">The video to take: the sure one, or the first suggestion when unsure.</param>
/// <param name="Candidates">The sure video or the suggestions, best first; empty when nothing was found.</param>
public sealed record SpotifyYtMatch(SpotifyMatchState State, SpotifyCandidate? Best, IReadOnlyList<SpotifyCandidate> Candidates);

/// <summary>
/// Finds a song of the Spotify history among YouTube results. Taken without asking is only a Topic upload
/// (the plain release, "Provided to YouTube by …") with the same artist, title, version and guests and the length
/// Spotify played; of several such uploads (album, remaster, compilation) the closest length, then the most watched.
/// Another version of the song (sped up, another remixer, live, another language) is never a candidate.
/// </summary>
public static partial class SpotifyYtMatcher
{
    public const double SURE_SCORE = 0.9;
    public const double UNSURE_TITLE_SCORE = 0.75;
    public const int MAX_SUGGESTIONS = 3;

    // words in brackets or dash suffixes that do not make another version
    private static readonly HashSet<string> HARMLESS_WORDS =
    [
        "remaster", "remastered", "version", "original", "radio", "edit", "single", "album", "explicit", "clean",
        "mono", "stereo", "bonus", "track", "deluxe", "edition",
    ];

    // Spotify and YouTube write guests also as "(with SZA)"
    [GeneratedRegex(@"[\(\[]\s*with\s+([^\)\]]+)[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex WithGuests();

    /// <summary>How far the length may differ from Spotify's: 3 seconds or 3 percent, whichever is more.</summary>
    public static bool DurationFits(int durationS, int expectedMs) =>
        Math.Abs(durationS * 1000 - expectedMs) <= Math.Max(3000, expectedMs * 0.03);

    /// <summary>Main artist, song name and the version words ("Vanic Remix", "Live"), for the YouTube Music song search.</summary>
    public static string MusicQuery(SpotifySong song)
    {
        var core = MbNormalizer.Core(song.Title);
        var version = SpotifyAnalyzer.VersionKey(song.Title) != SpotifyAnalyzer.VersionKey(core)
            ? " " + string.Join(' ', MbNormalizer.SearchWords(MbNormalizer.Qualifiers(song.Title)))
            : string.Empty;
        return $"{MainArtist(song.Artist)} {core}{version}";
    }

    /// <summary>The query for the plain YouTube search; "topic" pulls up the plain uploads.</summary>
    public static string SearchQuery(SpotifySong song) => $"{MusicQuery(song)} topic";

    /// <summary>Whether a song of the YouTube Music search, known only by its title, is worth fetching.</summary>
    public static bool TitleMatches(SpotifySong song, string title) =>
        SpotifyAnalyzer.VersionKey(title) == SpotifyAnalyzer.VersionKey(song.Title)
        && MbNormalizer.Similarity(MbNormalizer.Core(song.Title), MbNormalizer.Core(title)) >= SURE_SCORE;

    public static SpotifyYtMatch Match(SpotifySong song, IEnumerable<YtSearchResult> results)
    {
        var version = SpotifyAnalyzer.VersionKey(song.Title);
        var candidates = results
            .DistinctBy(r => r.VideoId)
            .Where(r => SpotifyAnalyzer.VersionKey(SongTitle(r).Title) == version)
            .Select(r => Judge(song, r))
            .Where(c => c.ArtistScore >= SURE_SCORE && c.TitleScore >= UNSURE_TITLE_SCORE)
            .ToList();

        var sure = candidates
            .Where(c => c.Video.IsTopic && c.TitleScore >= SURE_SCORE && c.DurationFits != false && c.OtherVersion is null)
            .OrderBy(c => DurationGap(song, c.Video))
            .ThenByDescending(c => c.Video.ViewCount ?? 0)
            .ToList();
        if (sure.Count > 0)
            return new SpotifyYtMatch(SpotifyMatchState.Sure, sure[0], [sure[0]]);

        var suggestions = candidates
            .OrderByDescending(c => c.OtherVersion is null)
            .ThenByDescending(c => c.Video.IsTopic)
            .ThenByDescending(c => c.DurationFits != false)
            .ThenByDescending(c => c.TitleScore)
            .ThenByDescending(c => c.Video.ViewCount ?? 0)
            .Take(MAX_SUGGESTIONS)
            .ToList();
        return suggestions.Count > 0
            ? new SpotifyYtMatch(SpotifyMatchState.Unsure, suggestions[0], suggestions)
            : new SpotifyYtMatch(SpotifyMatchState.NotFound, null, []);
    }

    private static SpotifyCandidate Judge(SpotifySong song, YtSearchResult video)
    {
        var (parsedArtist, title) = SongTitle(video);
        var titleScore = MbNormalizer.Similarity(MbNormalizer.Core(song.Title), MbNormalizer.Core(title));

        // the main artist, or a guest named in the title: Spotify lists "Home (with Machine Gun Kelly …)" under "mgk"
        var songGuests = Guests(song.Title);
        var accepted = songGuests.Prepend(MainArtist(song.Artist)).ToList();
        var channel = video.Channel is null ? null : MbNormalizer.CleanChannel(video.Channel);
        var names = new[] { channel, parsedArtist }.OfType<string>()
            .SelectMany(n => new[] { n, MainArtist(n) })
            .Concat(video.Artists ?? [])
            .ToList();
        var description = MbNormalizer.Normalize(video.Description);
        var artistScore = accepted.Max(a => Math.Max(
            names.Select(n => MbNormalizer.Similarity(a, n)).DefaultIfEmpty(0).Max(),
            // a Topic upload lists every artist: "Song · Coolio · L.V."
            video.IsTopic && MbNormalizer.ContainsWords(description, MbNormalizer.Normalize(a)) ? 1 : 0));

        bool? fits = song.EstimatedDurationMs is { } expected && video.DurationS is { } seconds ? DurationFits(seconds, expected) : null;
        return new SpotifyCandidate(video, titleScore, artistScore, fits, OtherVersion(song, video, title, songGuests));
    }

    /// <summary>Extra words in the video's brackets ("Collins String Version") or guests the song does not have.</summary>
    private static string? OtherVersion(SpotifySong song, YtSearchResult video, string title, IReadOnlyList<string> songGuests)
    {
        var known = QualifierWords(song.Title).ToHashSet(StringComparer.Ordinal);
        var extra = QualifierWords(title)
            .Where(w => !known.Contains(w) && !HARMLESS_WORDS.Contains(w) && !w.All(char.IsDigit))
            .ToList();
        if (extra.Count > 0)
            return string.Join(' ', extra);

        // "Nothin' on You (feat. Jay Park)" is not the song with Bruno Mars
        var main = MbNormalizer.Normalize(MainArtist(song.Artist));
        var videoGuests = Guests(title).Concat(video.Artists ?? [])
            .Select(MbNormalizer.Normalize)
            .Where(g => g != main)
            .ToHashSet(StringComparer.Ordinal);
        var expected = songGuests.Select(MbNormalizer.Normalize).ToList();
        return expected.Count > 0 && videoGuests.Count > 0 && !expected.Any(videoGuests.Contains) ? "other guests" : null;
    }

    private static IEnumerable<string> QualifierWords(string title) =>
        MbNormalizer.SearchWords(MbNormalizer.Qualifiers(WithAsFeaturing(title)));

    private static IReadOnlyList<string> Guests(string title) => MbNormalizer.SplitFeaturing(WithAsFeaturing(title)).Guests;

    private static string WithAsFeaturing(string title) => WithGuests().Replace(title, " (feat. $1)");

    /// <summary>A Topic upload is titled like the release; other videos usually read "Artist - Title (Official Video)".</summary>
    private static (string? Artist, string Title) SongTitle(YtSearchResult video) =>
        video.IsTopic ? (null, video.Title) : TitleParser.Parse(video.Title);

    private static int DurationGap(SpotifySong song, YtSearchResult video) =>
        song.EstimatedDurationMs is { } expected && video.DurationS is { } seconds
            ? Math.Abs(seconds * 1000 - expected)
            : int.MaxValue;

    private static string MainArtist(string artist) =>
        MbNormalizer.SplitArtists(MbNormalizer.SplitFeaturing(artist).Main).FirstOrDefault() ?? artist;
}
