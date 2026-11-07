using Tapedeck.Core.Models;
using Tapedeck.Core.Spotify;

namespace Tapedeck.Core;

/// <summary>The stored YouTube searches for Spotify songs and the user's decisions about them.</summary>
public interface ISpotifyMatchRepository
{
    /// <summary>All searches by song key.</summary>
    Task<IReadOnlyDictionary<string, SpotifySearchRecord>> ListAsync();

    /// <summary>Stores a search; a decision and a track of an earlier search of the song stay.</summary>
    Task SaveSearchAsync(string songKey, string query, IReadOnlyList<YtSearchResult> results);

    /// <summary>
    /// <see cref="SpotifyMatchState.Accepted"/> with the chosen video, <see cref="SpotifyMatchState.Rejected"/>,
    /// or <c>null</c> to let the matcher decide again.
    /// </summary>
    Task SetDecisionAsync(string songKey, SpotifyMatchState? decision, string? videoId = null);

    /// <summary>Links the song to its library track and marks it accepted. Needs a stored search.</summary>
    Task SetTrackAsync(string songKey, long trackId, string videoId);
}
