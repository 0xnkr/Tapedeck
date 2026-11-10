using Tapedeck.Core.Lyrics;
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
    /// <summary>Sets the square cover; a new one marks the file's tags as outdated, since the cover goes into the file.</summary>
    Task SetCoverAsync(long id, string coverPath, string coverSource);

    /// <summary>Tracks with a thumbnail but no square cover yet, oldest first.</summary>
    Task<IReadOnlyList<long>> ListWithoutCoverAsync();

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

    /// <summary>
    /// Tracks whose shown genres come from nowhere better than YouTube and that Last.fm was not asked for yet,
    /// with their shown artist, oldest first.
    /// </summary>
    Task<IReadOnlyList<(long Id, string Artist)>> ListWithoutGenresAsync();

    /// <summary>
    /// Replaces the track's Last.fm genres and remembers that Last.fm was asked, also when it had none.
    /// New genres mark the file's tags as outdated.
    /// </summary>
    Task SetLastFmTagsAsync(long trackId, IReadOnlyList<string> names);

    /// <summary>Records that the tags in the file now match the displayed values.</summary>
    Task MarkTagsWrittenAsync(long id);

    /// <summary>Integrated loudness of the track's file in LUFS; <c>null</c> when not measured yet.</summary>
    Task<double?> GetLoudnessAsync(long id);

    /// <summary>
    /// Stores the measured loudness of the track's file. A changed value marks the file's tags as outdated,
    /// because the track gain goes into the file.
    /// </summary>
    Task SetLoudnessAsync(long id, double lufs);

    /// <summary>
    /// Marks the tags of every tagged file with a measured loudness as outdated, because the gain fields change
    /// with the target player.
    /// </summary>
    /// <returns>The tracks marked.</returns>
    Task<IReadOnlyList<long>> MarkGainTagsOutdatedAsync();

    /// <summary>The track's lyrics from LRCLIB and from the user; <see cref="TrackLyrics.NONE"/> for an unknown track.</summary>
    Task<TrackLyrics> GetLyricsAsync(long id);

    /// <summary>
    /// Stores what LRCLIB has and remembers that it was asked. Lyrics that change what is shown mark the file's
    /// tags as outdated.
    /// </summary>
    Task SaveLrclibLyricsAsync(long id, LrclibLyrics lyrics);

    /// <summary>The user's own lyrics; <c>null</c> or blank lets LRCLIB's show again. Marks the tags as outdated.</summary>
    Task SetUserLyricsAsync(long id, string? text);

    /// <summary>
    /// Downloaded tracks LRCLIB was not asked for yet, and ones it had nothing for that MusicBrainz has given
    /// better names since; oldest first.
    /// </summary>
    Task<IReadOnlyList<LyricsRequest>> ListWithoutLyricsAsync();

    /// <summary>The track as LRCLIB is asked for it.</summary>
    Task<LyricsRequest?> GetLyricsRequestAsync(long id);

    /// <summary>Downloaded tracks whose file was never measured, oldest first.</summary>
    Task<IReadOnlyList<long>> ListWithoutLoudnessAsync();

    /// <summary>The track a merged duplicate's video now belongs to; <c>null</c> when the video is no alias.</summary>
    Task<long?> GetAliasTargetAsync(string videoId);

    /// <summary>Videos of merged duplicates and the tracks they belong to now.</summary>
    Task<IReadOnlyDictionary<string, long>> ListAliasesAsync();

    /// <summary>
    /// Makes <paramref name="mergedIds"/> part of <paramref name="keepId"/>, in one step: their videos become aliases
    /// of the kept track, their playlist places go to it (or are dropped where it is in the playlist already), and
    /// their best rating is taken over when the kept track has none. The merged tracks themselves stay; removing
    /// them with their files is up to the caller.
    /// </summary>
    Task MergeIntoAsync(long keepId, IReadOnlyList<long> mergedIds);

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
