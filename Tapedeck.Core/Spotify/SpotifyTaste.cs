using Tapedeck.Core.Recommendations;

namespace Tapedeck.Core.Spotify;

/// <summary>
/// Counts per library track what the streaming history says about the user's taste: real plays, early skips and
/// the last day played. Only raw numbers; the phone app applies its own decay over time.
/// </summary>
public static class SpotifyTaste
{
    /// <summary>A song skipped with the forward button before this is one the user did not want to hear.</summary>
    public const int EARLY_SKIP_MS = 10_000;

    private const string FORWARD_BUTTON = "fwdbtn";

    public static bool IsEarlySkip(SpotifyStream stream) =>
        stream.ReasonEnd == FORWARD_BUTTON && stream.MsPlayed < EARLY_SKIP_MS;

    /// <param name="tracksBySongKey">The library tracks by song, from <see cref="SpotifyLibraryMatch.TracksBySongKey"/>.</param>
    /// <returns>The tracks with at least one play or early skip.</returns>
    public static IReadOnlyDictionary<long, ListeningHistory> ByTrack(IEnumerable<SpotifyStream> streams,
        IReadOnlyDictionary<string, long> tracksBySongKey, SpotifyPlayRules? rules = null)
    {
        rules ??= SpotifyPlayRules.DEFAULT;
        var counts = new Dictionary<long, (int Plays, int Skips, DateTimeOffset Last)>();
        foreach (var stream in streams)
        {
            if (stream.Incognito && !rules.CountIncognito)
                continue;
            var play = rules.IsRealPlay(stream);
            var skip = !play && IsEarlySkip(stream);
            if (!play && !skip)
                continue;
            if (!tracksBySongKey.TryGetValue(SpotifyAnalyzer.SongKey(stream.Artist, stream.Title), out var trackId))
                continue;

            var (plays, skips, last) = counts.GetValueOrDefault(trackId);
            counts[trackId] = (plays + (play ? 1 : 0), skips + (skip ? 1 : 0), stream.EndedAt > last ? stream.EndedAt : last);
        }
        return counts.ToDictionary(
            c => c.Key,
            c => new ListeningHistory(c.Value.Plays, c.Value.Skips, DateOnly.FromDateTime(c.Value.Last.UtcDateTime)));
    }
}
