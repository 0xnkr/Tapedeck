using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tapedeck.Core.Spotify;

/// <param name="Tracks">Library tracks the history has plays or early skips of.</param>
/// <param name="Changed">Tracks whose stored history changed; their file tags are outdated now.</param>
public sealed record SpotifyTasteResult(int Tracks, IReadOnlyList<long> Changed);

/// <summary>
/// Takes over plays and early skips of a Spotify streaming history for the library tracks (<see cref="SpotifyTaste"/>),
/// as the starting taste of the phone app's recommendations. Replaces what an earlier history left: a newer data
/// download contains the older years, too.
/// </summary>
public sealed class SpotifyTasteService(ITrackRepository tracks, ISpotifyMatchRepository matches, ILogger<SpotifyTasteService>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    public async Task<SpotifyTasteResult> ApplyAsync(SpotifyHistory history)
    {
        var bySongKey = SpotifyLibraryMatch.TracksBySongKey(await tracks.ListDisplayAsync(), await matches.ListAsync());
        var byTrack = SpotifyTaste.ByTrack(history.Streams, bySongKey);
        var changed = await tracks.SetListeningHistoryAsync(byTrack);
        this.log.LogInformation("Spotify history for recommendations: {Tracks} library tracks with plays or early skips, {Changed} changed",
            byTrack.Count, changed.Count);
        return new SpotifyTasteResult(byTrack.Count, changed);
    }
}
