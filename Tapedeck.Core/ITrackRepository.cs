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

    /// <summary>Stores the user's corrections and marks the file's tags as outdated.</summary>
    Task SetUserFieldsAsync(long id, UserFields fields);

    /// <summary>The rating is not written into the file, so it does not mark the tags as outdated.</summary>
    Task SetRatingAsync(long id, int? rating);

    Task AddTagAsync(long trackId, string name, TagSource source);

    /// <summary>
    /// Replaces the user's genres of a track and marks the file's tags as outdated.
    /// An empty list removes them, so the next best genre source applies again.
    /// </summary>
    Task SetUserTagsAsync(long trackId, IReadOnlyList<string> names);

    /// <summary>Records that the tags in the file now match the displayed values.</summary>
    Task MarkTagsWrittenAsync(long id);
}
