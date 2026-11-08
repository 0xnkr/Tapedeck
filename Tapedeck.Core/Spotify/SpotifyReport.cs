using System.Globalization;
using System.Text;

namespace Tapedeck.Core.Spotify;

/// <summary>
/// A Markdown overview of a streaming history, to check the play rules and song grouping on real data
/// before importing: counts per year, top artists and songs, thresholds and merged spellings.
/// </summary>
public static class SpotifyReport
{
    private const int TOP_ARTISTS = 50;
    private const int TOP_SONGS = 200;
    private const int MERGED_SAMPLE = 40;
    private static readonly int[] THRESHOLDS = [1, 5, 10, 20, 50, 100];
    private static readonly CultureInfo INVARIANT = CultureInfo.InvariantCulture;

    /// <param name="library">Library tracks by song key; <c>null</c> when the library could not be read.</param>
    public static string ToMarkdown(SpotifyHistory history, SpotifyPlayRules? rules = null, IReadOnlyDictionary<string, long>? library = null)
    {
        rules ??= SpotifyPlayRules.DEFAULT;
        var streams = history.Streams;
        var songs = SpotifyAnalyzer.Analyze(streams, rules);
        var played = songs.Where(s => s.RealPlays > 0).ToList();
        var stats = history.Stats;
        var md = new StringBuilder();

        md.AppendLine("# Spotify streaming history").AppendLine();
        md.AppendLine($"- Real play: {rules.Describe()}");
        md.AppendLine($"- Files: {stats.Files}, rows: {N(stats.Rows)}, song plays: {N(streams.Count)}");
        md.AppendLine($"- Skipped: {N(stats.Episodes)} episodes, {N(stats.NoMetadata)} rows without a track, {N(stats.Duplicates)} duplicates");
        if (streams.Count > 0)
            md.AppendLine($"- Period: {D(streams[0].EndedAt)} to {D(streams[^1].EndedAt)}");
        md.AppendLine($"- Listened: {H(streams.Sum(s => (long)s.MsPlayed))} h, real plays: {N(played.Sum(s => s.RealPlays))}");
        md.AppendLine($"- Songs: {N(songs.Count)} heard, {N(played.Count)} with a real play, from {N(streams.Select(s => s.TrackUri).Distinct().Count())} Spotify tracks");
        md.AppendLine(library is null ? "- Library: not read" : $"- Library: {N(library.Count)} songs");
        md.AppendLine();

        md.AppendLine("## Per year").AppendLine();
        md.AppendLine("| Year | Plays | Real plays | Hours | Songs with a real play |");
        md.AppendLine("|---|--:|--:|--:|--:|");
        foreach (var year in streams.GroupBy(s => s.EndedAt.Year).OrderBy(g => g.Key))
        {
            var real = year.Where(rules.IsRealPlay).ToList();
            var yearSongs = real.Select(s => SpotifyAnalyzer.SongKey(s.Artist, s.Title)).Distinct().Count();
            md.AppendLine($"| {year.Key} | {N(year.Count())} | {N(real.Count)} | {H(year.Sum(s => (long)s.MsPlayed))} | {N(yearSongs)} |");
        }
        md.AppendLine();

        md.AppendLine("## Thresholds").AppendLine();
        md.AppendLine("| Real plays | Songs | In library | Hours |");
        md.AppendLine("|--:|--:|--:|--:|");
        foreach (var min in THRESHOLDS)
        {
            var hits = played.Where(s => s.RealPlays >= min).ToList();
            var inLibrary = library is null ? "–" : N(hits.Count(s => library.ContainsKey(s.Key)));
            md.AppendLine($"| ≥ {min} | {N(hits.Count)} | {inLibrary} | {H(hits.Sum(s => s.MsPlayed))} |");
        }
        if (played.Count >= TOP_SONGS)
            md.AppendLine().AppendLine($"The top {TOP_SONGS} need at least {played[TOP_SONGS - 1].RealPlays} real plays.");
        md.AppendLine();

        md.AppendLine($"## Top {TOP_ARTISTS} artists").AppendLine();
        md.AppendLine("| # | Artist | Real plays | Songs | Hours |");
        md.AppendLine("|--:|---|--:|--:|--:|");
        var artists = played
            .GroupBy(s => SpotifyAnalyzer.ArtistKey(s.Artist), StringComparer.Ordinal)
            .Select(g => (Name: g.OrderByDescending(s => s.RealPlays).First().Artist, Plays: g.Sum(s => s.RealPlays), Songs: g.Count(), Ms: g.Sum(s => s.MsPlayed)))
            .OrderByDescending(a => a.Plays)
            .Take(TOP_ARTISTS);
        var rank = 0;
        foreach (var artist in artists)
            md.AppendLine($"| {++rank} | {E(artist.Name)} | {N(artist.Plays)} | {artist.Songs} | {H(artist.Ms)} |");
        md.AppendLine();

        md.AppendLine($"## Top {TOP_SONGS} songs").AppendLine();
        md.AppendLine("| # | Artist | Title | Real plays | Plays | Hours | First | Last | Tracks | Library |");
        md.AppendLine("|--:|---|---|--:|--:|--:|---|---|--:|:-:|");
        rank = 0;
        foreach (var song in played.Take(TOP_SONGS))
        {
            var inLibrary = library is null ? "–" : library.ContainsKey(song.Key) ? "✓" : string.Empty;
            md.AppendLine($"| {++rank} | {E(song.Artist)} | {E(song.Title)} | {song.RealPlays} | {song.Streams} | {H(song.MsPlayed)} | {D(song.FirstPlayed)} | {D(song.LastPlayed)} | {song.TrackUris.Count} | {inLibrary} |");
        }
        md.AppendLine();

        md.AppendLine("## Merged spellings").AppendLine();
        md.AppendLine("The most played songs that Spotify lists under different names; each line should be one song.").AppendLine();
        foreach (var song in played.Where(s => s.Spellings.Count > 1).Take(MERGED_SAMPLE))
            md.AppendLine($"- **{E(song.Artist)} – {E(song.Title)}** ({song.RealPlays}): {string.Join(" · ", song.Spellings.Skip(1).Select(E))}");

        return md.ToString();
    }

    private static string N(int value) => value.ToString("#,0", INVARIANT);

    private static string H(long ms) => (ms / 3_600_000.0).ToString("#,0.0", INVARIANT);

    private static string D(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd", INVARIANT);

    // pipes would split the table cell
    private static string E(string text) => text.Replace("|", "\\|");
}
