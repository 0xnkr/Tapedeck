using System.Globalization;
using System.Text;

namespace Tapedeck.Core.Spotify;

/// <summary>
/// A Markdown overview of the YouTube searches for Spotify songs: how many are sure, unsure or not found, and why,
/// to tune the matching rules on real data.
/// </summary>
public static class SpotifyMatchReport
{
    private static readonly CultureInfo INVARIANT = CultureInfo.InvariantCulture;

    /// <param name="songs">The songs in import order; songs without a stored search are listed as not searched.</param>
    public static string ToMarkdown(IReadOnlyList<SpotifySong> songs, IReadOnlyDictionary<string, SpotifySearchRecord> searches)
    {
        var rows = songs.Select((song, i) => (Rank: i + 1, Song: song, Search: searches.GetValueOrDefault(song.Key))).ToList();
        var judged = rows.Where(r => r.Search is not null)
            .Select(r => (r.Rank, r.Song, Search: r.Search!, Match: SpotifyYtMatcher.Match(r.Song, r.Search!.Results)))
            .ToList();
        var md = new StringBuilder();

        md.AppendLine("# Spotify songs on YouTube").AppendLine();
        md.AppendLine($"- Songs: {songs.Count}, searched: {judged.Count}");
        foreach (var state in new[] { SpotifyMatchState.Sure, SpotifyMatchState.Unsure, SpotifyMatchState.NotFound })
            md.AppendLine($"- {state}: {judged.Count(j => j.Match.State == state)}");
        md.AppendLine($"- Known length (from plays to the end): {songs.Count(s => s.EstimatedDurationMs is not null)} of {songs.Count}");
        md.AppendLine();

        md.AppendLine("## Sure").AppendLine();
        md.AppendLine("| # | Song | Video | Channel | Length | Views |");
        md.AppendLine("|--:|---|---|---|--:|--:|");
        foreach (var (rank, song, _, match) in judged.Where(j => j.Match.State == SpotifyMatchState.Sure))
        {
            var video = match.Best!.Video;
            md.AppendLine($"| {rank} | {E(song.Artist)} – {E(song.Title)} | {E(video.Title)} | {E(video.Channel)} | {Length(song, video.DurationS)} | {Views(video.ViewCount)} |");
        }
        md.AppendLine();

        md.AppendLine("## Unsure").AppendLine();
        foreach (var (rank, song, search, match) in judged.Where(j => j.Match.State == SpotifyMatchState.Unsure))
        {
            md.AppendLine($"**{rank}. {E(song.Artist)} – {E(song.Title)}** ({song.RealPlays} plays) · query `{search.Query}`").AppendLine();
            foreach (var candidate in match.Candidates)
            {
                var video = candidate.Video;
                md.AppendLine($"- [{E(video.Title)}](https://www.youtube.com/watch?v={video.VideoId}) · {E(video.Channel)} · {Length(song, video.DurationS)} · {Views(video.ViewCount)} views — {string.Join(", ", candidate.Problems(song))}");
            }
            md.AppendLine();
        }

        md.AppendLine("## Not found").AppendLine();
        foreach (var (rank, song, search, _) in judged.Where(j => j.Match.State == SpotifyMatchState.NotFound))
        {
            var first = search.Results.Take(3).Select(r => $"{E(r.Title)} ({E(r.Channel)}{(r.IsTopic ? ", Topic" : string.Empty)})");
            md.AppendLine($"- **{rank}. {E(song.Artist)} – {E(song.Title)}** · query `{search.Query}` · first results: {string.Join(" · ", first)}");
        }

        var unsearched = rows.Where(r => r.Search is null).ToList();
        if (unsearched.Count > 0)
            md.AppendLine().AppendLine($"Not searched yet: {unsearched.Count} songs.");
        return md.ToString();
    }

    private static string Length(SpotifySong song, int? seconds)
    {
        var video = seconds is { } s ? $"{s / 60}:{s % 60:00}" : "?";
        if (song.EstimatedDurationMs is not { } expected)
            return video;
        var spotify = expected / 1000;
        return $"{video} / {spotify / 60}:{spotify % 60:00}";
    }

    private static string Views(long? views) => views is { } v ? v.ToString("#,0", INVARIANT) : "?";

    private static string E(string? text) => (text ?? string.Empty).Replace("|", "\\|").Replace("[", "\\[").Replace("]", "\\]");
}
