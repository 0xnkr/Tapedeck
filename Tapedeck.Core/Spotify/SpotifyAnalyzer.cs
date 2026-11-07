using System.Text.RegularExpressions;
using Tapedeck.Core.MusicBrainz;

namespace Tapedeck.Core.Spotify;

/// <summary>A song of the history with all its plays, across every Spotify track (album, single, deluxe) of it.</summary>
/// <param name="Key">See <see cref="SpotifyAnalyzer.SongKey"/>.</param>
/// <param name="Artist">The spelling heard most often.</param>
/// <param name="Title">The spelling heard most often, together with <paramref name="Artist"/>.</param>
/// <param name="TrackUris">Most played first.</param>
/// <param name="Streams">All plays, also the short and skipped ones.</param>
/// <param name="FirstPlayed">First play of any length.</param>
/// <param name="LastPlayed">Last play of any length.</param>
/// <param name="EstimatedDurationMs">Median of the plays that ran to the end; <c>null</c> when none did.</param>
/// <param name="Spellings">The different "Artist – Title" spellings, most heard first.</param>
public sealed record SpotifySong(
    string Key,
    string Artist,
    string Title,
    string? Album,
    IReadOnlyList<string> TrackUris,
    int RealPlays,
    int Streams,
    long MsPlayed,
    DateTimeOffset FirstPlayed,
    DateTimeOffset LastPlayed,
    IReadOnlyDictionary<int, int> RealPlaysPerYear,
    int? EstimatedDurationMs,
    IReadOnlyList<string> Spellings);

/// <summary>Groups the plays of a history into songs and counts the real plays.</summary>
public static partial class SpotifyAnalyzer
{
    // a version in another language, e.g. "Japanese Version"; beyond the MbVariant flags
    private const int OTHER_LANGUAGE = 1 << 20;

    // how Spotify names versions besides the words MbNormalizer knows; matched on normalized qualifiers
    [GeneratedRegex(@"\b(?:club|extended|dub) mix\b")]
    private static partial Regex ClubMix();

    [GeneratedRegex(@"\b(?:tour|concert|ao vivo|en vivo)\b")]
    private static partial Regex Concert();

    [GeneratedRegex(@"\b(?:stripped|piano version)\b")]
    private static partial Regex Stripped();

    [GeneratedRegex(@"\b(?:japanese|korean|chinese|english|spanish|french|german|deutsche|italian|portuguese) version\b")]
    private static partial Regex Language();

    /// <summary>
    /// Main artist, song name and versions, without case, accents and punctuation. Spotify lists the same song
    /// under several tracks ("Song", "Song - Remastered 2011", "Song (feat. X)" on the deluxe album,
    /// "Song - Radio Edit"), which share the key; a remix, club mix, live, stripped, instrumental or other-language
    /// version gets its own. Library tracks use the same key.
    /// </summary>
    public static string SongKey(string artist, string title)
    {
        var mainArtist = MbNormalizer.SplitArtists(MbNormalizer.SplitFeaturing(artist).Main).FirstOrDefault() ?? artist;
        var versions = Versions(title);
        // every remixer makes another song: "PNAU Remix" is not "Claptone Remix"
        var remix = (versions & (int)MbVariant.Remix) != 0 ? MbNormalizer.Normalize(MbNormalizer.Qualifiers(title)) : string.Empty;
        return $"{MbNormalizer.Normalize(mainArtist)}\n{MbNormalizer.Normalize(MbNormalizer.Core(title))}\n{versions}\n{remix}";
    }

    private static int Versions(string title)
    {
        // only brackets and dash suffixes name a version; "Live Your Life" is no live recording
        var qualifiers = MbNormalizer.Qualifiers(title);
        var normalized = MbNormalizer.Normalize(qualifiers);
        // a radio or single edit is the version the song is known by
        var variants = MbNormalizer.Variants(qualifiers) & ~MbVariant.Edit;
        if (ClubMix().IsMatch(normalized))
            variants |= MbVariant.Remix;
        if (Concert().IsMatch(normalized))
            variants |= MbVariant.Live;
        if (Stripped().IsMatch(normalized))
            variants |= MbVariant.Acoustic;
        return (int)variants | (Language().IsMatch(normalized) ? OTHER_LANGUAGE : 0);
    }

    /// <summary>The main artist of <see cref="SongKey"/>, for grouping by artist.</summary>
    public static string ArtistKey(string artist) =>
        MbNormalizer.Normalize(MbNormalizer.SplitArtists(MbNormalizer.SplitFeaturing(artist).Main).FirstOrDefault() ?? artist);

    /// <summary>Every song with at least one stream, most real plays first, then most time played.</summary>
    public static IReadOnlyList<SpotifySong> Analyze(IEnumerable<SpotifyStream> streams, SpotifyPlayRules? rules = null)
    {
        rules ??= SpotifyPlayRules.DEFAULT;
        return streams
            .GroupBy(s => SongKey(s.Artist, s.Title), StringComparer.Ordinal)
            .Select(g => ToSong(g.Key, g.ToList(), rules))
            .OrderByDescending(s => s.RealPlays)
            .ThenByDescending(s => s.MsPlayed)
            .ThenBy(s => s.Artist, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SpotifySong ToSong(string key, IReadOnlyList<SpotifyStream> plays, SpotifyPlayRules rules)
    {
        var real = plays.Where(rules.IsRealPlay).ToList();
        var spellings = MostHeard(plays, s => $"{s.Artist} – {s.Title}");
        var name = plays.Where(s => $"{s.Artist} – {s.Title}" == spellings[0]).First();
        var completed = plays.Where(s => s.ReasonEnd == "trackdone").Select(s => s.MsPlayed).Order().ToList();

        return new SpotifySong(
            Key: key,
            Artist: name.Artist,
            Title: name.Title,
            Album: MostHeard(plays.Where(s => s.Album is not null), s => s.Album!).FirstOrDefault(),
            TrackUris: MostHeard(plays, s => s.TrackUri),
            RealPlays: real.Count,
            Streams: plays.Count,
            MsPlayed: plays.Sum(s => (long)s.MsPlayed),
            FirstPlayed: plays.Min(s => s.EndedAt),
            LastPlayed: plays.Max(s => s.EndedAt),
            RealPlaysPerYear: real.GroupBy(s => s.EndedAt.Year).ToDictionary(g => g.Key, g => g.Count()),
            EstimatedDurationMs: completed.Count == 0 ? null : completed[completed.Count / 2],
            Spellings: spellings);
    }

    /// <summary>Distinct values, most frequent first; on a tie the one heard last wins.</summary>
    private static List<string> MostHeard(IEnumerable<SpotifyStream> plays, Func<SpotifyStream, string> value) =>
        plays.GroupBy(value, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(s => s.EndedAt))
            .Select(g => g.Key)
            .ToList();
}
