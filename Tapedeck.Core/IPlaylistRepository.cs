using Tapedeck.Core.Models;
using Tapedeck.Core.Playlists;

namespace Tapedeck.Core;

public interface IPlaylistRepository
{
    /// <summary>All playlists by name, with their track counts.</summary>
    Task<IReadOnlyList<Playlist>> ListAsync();

    Task<Playlist?> GetAsync(long id);

    /// <summary>Whether a playlist with this name exists, ignoring case; <paramref name="exceptId"/> is left out.</summary>
    Task<bool> NameExistsAsync(string name, long? exceptId = null);

    /// <summary>The playlist linked to this YouTube playlist, if it was imported before.</summary>
    Task<Playlist?> FindBySourceAsync(string sourceUrl);

    /// <param name="sourceUrl">The YouTube playlist to link, or <c>null</c>.</param>
    /// <returns>The new playlist's id.</returns>
    Task<long> CreateAsync(string name, string? sourceUrl = null);

    /// <returns>The new smart playlist's id.</returns>
    Task<long> CreateSmartAsync(string name, SmartRules rules);

    /// <summary>Changes name and rules of a smart playlist.</summary>
    Task UpdateSmartAsync(long id, string name, SmartRules rules);

    Task RenameAsync(long id, string name);

    /// <summary>Deletes the playlist; its tracks stay in the library.</summary>
    Task DeleteAsync(long id);

    /// <summary>The playlist's tracks in their order; for a smart playlist picked from the library by its rules.</summary>
    Task<IReadOnlyList<TrackDisplay>> GetTracksAsync(long playlistId);

    /// <summary>Appends tracks at the end; tracks already in the playlist are skipped. A smart playlist takes none.</summary>
    /// <returns>Number of tracks actually added.</returns>
    Task<int> AddTracksAsync(long playlistId, IReadOnlyList<long> trackIds);

    Task RemoveTrackAsync(long playlistId, long trackId);

    /// <summary>Moves a track to <paramref name="newIndex"/> (0-based); the others close up without gaps.</summary>
    Task MoveTrackAsync(long playlistId, long trackId, int newIndex);
}
