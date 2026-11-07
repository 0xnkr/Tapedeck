using Tapedeck.Core.Models;

namespace Tapedeck.Core.Spotify;

/// <summary>Finds the songs of the history that are already in the library, by artist and title as shown.</summary>
public static class SpotifyLibraryMatch
{
    /// <summary>The library tracks by <see cref="SpotifyAnalyzer.SongKey"/>; of several videos of a song the oldest track.</summary>
    public static IReadOnlyDictionary<string, long> TracksBySongKey(IEnumerable<TrackDisplay> tracks)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var track in tracks.Where(t => t.Artist.Length > 0 && t.Title.Length > 0).OrderBy(t => t.Id))
            result.TryAdd(SpotifyAnalyzer.SongKey(track.Artist, track.Title), track.Id);
        return result;
    }
}
