using Tapedeck.Core.Models;
using Tapedeck.Core.MusicBrainz;

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

    /// <summary>Points the track to its renamed file (relative to the library).</summary>
    Task SetFilePathAsync(long id, string filePath);

    /// <summary>Removes the track with its genres and playlist entries. Files are up to the caller.</summary>
    Task DeleteAsync(long id);

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

    /// <summary>Tracks in the given MusicBrainz states, oldest first, e.g. the ones never searched.</summary>
    Task<IReadOnlyList<long>> ListByMbStateAsync(params MbMatchState[] states);

    /// <summary>
    /// Stores the outcome of a MusicBrainz search in one step.
    /// With <paramref name="applied"/> its values, release and genres are taken over and the state is
    /// <see cref="MbMatchState.Matched"/>; without, an earlier match is removed together with its genres and
    /// its MusicBrainz cover. Either way a change of the shown values marks the file's tags as outdated.
    /// </summary>
    /// <param name="score">How well the best song matched; ignored with <paramref name="applied"/>, which has its own.</param>
    /// <param name="candidatesJson">The best songs found, for confirming a suggestion later; <c>null</c> keeps the stored ones.</param>
    Task SaveMbResultAsync(long id, MbMatchState state, MbPick? applied, IReadOnlyList<string> genres,
        double? score, string? candidatesJson);
}
