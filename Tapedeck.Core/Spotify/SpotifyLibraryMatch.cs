using Tapedeck.Core.Models;

namespace Tapedeck.Core.Spotify;

/// <summary>Finds the songs of the history that are already in the library.</summary>
public static class SpotifyLibraryMatch
{
    /// <summary>
    /// The library tracks by <see cref="SpotifyAnalyzer.SongKey"/>; of several videos of a song the oldest track.
    /// A song imported before leads to its track even when the video is titled differently.
    /// </summary>
    /// <param name="searches">Stored searches whose <see cref="SpotifySearchRecord.TrackId"/> links a song to its track.</param>
    public static IReadOnlyDictionary<string, long> TracksBySongKey(IEnumerable<TrackDisplay> tracks,
        IReadOnlyDictionary<string, SpotifySearchRecord>? searches = null)
    {
        var list = tracks.ToList();
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (searches is not null)
        {
            var ids = list.Select(t => t.Id).ToHashSet();
            foreach (var (key, search) in searches)
            {
                if (search.TrackId is { } id && ids.Contains(id))
                    result[key] = id;
            }
        }
        foreach (var track in list.Where(t => t.Artist.Length > 0 && t.Title.Length > 0).OrderBy(t => t.Id))
            result.TryAdd(SpotifyAnalyzer.SongKey(track.Artist, track.Title), track.Id);
        return result;
    }
}
