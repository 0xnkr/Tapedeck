using Tapedeck.Core.Models;
using Tapedeck.Core.Recommendations;

namespace Tapedeck.Core.Services;

/// <summary>
/// Everything a tag writer puts into a file, independent of the format: the shown values, the loudness,
/// the lyrics and the recommendation fields.
/// </summary>
/// <param name="Track">The values from <c>tracks_display</c>.</param>
/// <param name="Loudness">Integrated loudness in LUFS, <c>null</c> without a measurement.</param>
/// <param name="LyricsText">The shown lyrics with LF line ends, <c>null</c> without any.</param>
/// <param name="LyricsSynced">Whether <paramref name="LyricsText"/> is timed LRC.</param>
/// <param name="ArtistTags">Formatted <c>TAPEDECK_TAGS</c> value or <c>null</c>.</param>
/// <param name="History">Formatted <c>TAPEDECK_HISTORY</c> value or <c>null</c>.</param>
/// <param name="Sound">Formatted <c>TAPEDECK_AUDIO</c> value or <c>null</c>.</param>
public sealed record TrackTagData(
    TrackDisplay Track,
    double? Loudness,
    string? LyricsText,
    bool LyricsSynced,
    string? ArtistTags,
    string? History,
    string? Sound)
{
    /// <summary>Loads the tag data of a track that has a file.</summary>
    public static async Task<TrackTagData> LoadAsync(ITrackRepository repository, long trackId)
    {
        var track = await repository.GetDisplayAsync(trackId)
                    ?? throw new InvalidOperationException($"Track {trackId} does not exist.");
        if (track.FilePath is null)
            throw new InvalidOperationException($"Track {trackId} has no file yet.");

        var loudness = await repository.GetLoudnessAsync(trackId);
        var lyrics = await repository.GetLyricsAsync(trackId);
        var lyricsText = lyrics.Text?.Replace("\r\n", "\n").Trim();
        if (lyricsText is { Length: 0 })
            lyricsText = null;

        // for the phone app's recommendations: the artist's Last.fm tags, the Spotify history and the sound
        var artistTags = RecommendationTags.FormatTags(await repository.GetArtistTagsAsync(RecommendationTags.ArtistKey(track.Artist)));
        var history = RecommendationTags.FormatHistory(await repository.GetListeningHistoryAsync(trackId));
        var sound = await repository.GetAudioFeaturesAsync(trackId);
        return new TrackTagData(track, loudness, lyricsText, lyrics.IsSynced, artistTags, history, sound);
    }
}
