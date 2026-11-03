using Tapedeck.Core.Models;

namespace Tapedeck.Core;

public interface ITrackRepository
{
    /// <summary>Adds a track. If the <paramref name="videoId"/> already exists, its id is returned.</summary>
    Task<long> AddAsync(string videoId, string url);

    Task<Track?> GetAsync(long id);
    Task<TrackDisplay?> GetDisplayAsync(long id);
    Task<IReadOnlyList<TrackDisplay>> ListDisplayAsync();

    Task UpdateYtMetadataAsync(long id, YtMetadata metadata);
    Task UpdateDownloadStateAsync(long id, DownloadStatus status, string? filePath = null, string? fileFormat = null, string? errorMsg = null);

    /// <summary>
    /// Sets tracks left in <c>downloading</c> by a closed or crashed app back to <c>pending</c>.
    /// Call once at startup, before any download runs.
    /// </summary>
    /// <returns>Number of reset tracks.</returns>
    Task<int> ResetInterruptedDownloadsAsync();
    Task SetThumbPathAsync(long id, string thumbPath);
    Task SetCoverAsync(long id, string coverPath, string coverSource);

    Task SetUserFieldsAsync(long id, UserFields fields);
    Task SetRatingAsync(long id, int? rating);

    Task AddTagAsync(long trackId, string name, TagSource source);
}
