using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using Tapedeck.Core.Lyrics;
using Tapedeck.Core.Recommendations;
using Tapedeck.Core.Settings;

namespace Tapedeck.Core.Services;

/// <summary>
/// Writes the values from <c>tracks_display</c> into the audio file. Android players sort by these tags,
/// not by folders. Repeatable: existing tags are overwritten, not appended.
/// </summary>
/// <param name="settings">Tells the target player; without it, the files follow the standard.</param>
public sealed class TagWriter(ITrackRepository repository, AppPaths paths, SettingsStore? settings = null, ILogger<TagWriter>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    /// <summary>Opus track gain (RFC 7845), read by Poweramp, Musicolet and most other players.</summary>
    public const string TRACK_GAIN_FIELD = "R128_TRACK_GAIN";

    /// <summary>Only for target players that need it (<see cref="PlayerQuirks.ReplayGain"/>); Opus players should use R128.</summary>
    public const string REPLAYGAIN_FIELD = "REPLAYGAIN_TRACK_GAIN";

    /// <summary>The lyrics, as most players and taggers name the field in Vorbis comments.</summary>
    public const string LYRICS_FIELD = "LYRICS";

    /// <summary>ffmpeg reports this for silence; there is nothing to level out.</summary>
    private const double SILENCE_LUFS = -70;

    public async Task WriteTagsAsync(long trackId)
    {
        var track = await repository.GetDisplayAsync(trackId)
                    ?? throw new InvalidOperationException($"Track {trackId} does not exist.");
        if (track.FilePath is null)
            throw new InvalidOperationException($"Track {trackId} has no file yet.");

        var filePath = paths.ToLibraryPath(track.FilePath);
        var loudness = await repository.GetLoudnessAsync(trackId);
        var quirks = PlayerQuirks.Of(TargetPlayerCode.Parse(settings?.Load().TargetPlayer));
        var lyrics = await repository.GetLyricsAsync(trackId);
        var lyricsText = lyrics.Text?.Replace("\r\n", "\n").Trim();
        if (lyricsText is { Length: 0 })
            lyricsText = null;
        // for the phone app's recommendations: the artist's Last.fm tags, the Spotify history and the sound
        var artistTags = RecommendationTags.FormatTags(await repository.GetArtistTagsAsync(RecommendationTags.ArtistKey(track.Artist)));
        var history = RecommendationTags.FormatHistory(await repository.GetListeningHistoryAsync(trackId));
        var sound = await repository.GetAudioFeaturesAsync(trackId);
        await Task.Run(() =>
        {
            // files damaged by an earlier write could not even be opened
            if (OggCommentRepair.Repair(filePath))
                this.log.LogWarning("Repaired the tags of {File} before writing (TagLib# had left out their end)", track.FilePath);
            using (var file = TagLib.File.Create(filePath))
            {
                var tag = file.Tag;
                tag.Title = track.Title;
                tag.Performers = string.IsNullOrEmpty(track.Artist) ? [] : [track.Artist];
                tag.AlbumArtists = string.IsNullOrEmpty(track.AlbumArtist) ? [] : [track.AlbumArtist];
                tag.Album = track.Album;
                tag.Year = (uint)(track.Year ?? 0);
                // the stable main genres sort a phone's library; the fine ones only when none of them maps
                tag.Genres = track.MainGenreList.Count > 0 ? [.. track.MainGenreList] : [.. track.GenreList];
                // place on the album and the MusicBrainz ids (as Picard names them), only with a match.
                // Empty instead of null clears a field: TagLib's Ogg setter of the artist id throws on null.
                tag.Track = (uint)(track.TrackNo ?? 0);
                tag.Disc = (uint)(track.DiscNo ?? 0);
                tag.MusicBrainzTrackId = track.MbRecordingId ?? string.Empty;
                tag.MusicBrainzReleaseId = track.MbReleaseId ?? string.Empty;
                tag.MusicBrainzReleaseGroupId = track.MbReleaseGroupId ?? string.Empty;
                tag.MusicBrainzArtistId = track.MbArtistId ?? string.Empty;
                tag.Comment = track.Url;
                tag.Pictures = track.Cover is { } cover && File.Exists(cover)
                    ? [new TagLib.Picture(cover) { Type = TagLib.PictureType.FrontCover }]
                    : [];
                // a file without a measurement gets no gain rather than an outdated one; the ReplayGain field
                // goes again when the target player no longer needs it
                if (file.GetTag(TagLib.TagTypes.Xiph, create: true) is TagLib.Ogg.XiphComment xiph)
                {
                    var measured = loudness is > SILENCE_LUFS ? loudness : null;
                    if (measured is { } lufs)
                        xiph.SetField(TRACK_GAIN_FIELD, LoudnessMeter.TrackGain(lufs).ToString(CultureInfo.InvariantCulture));
                    else
                        xiph.RemoveField(TRACK_GAIN_FIELD);

                    if (measured is { } level && quirks.ReplayGain)
                        xiph.SetField(REPLAYGAIN_FIELD, LoudnessMeter.ReplayGain(level));
                    else
                        xiph.RemoveField(REPLAYGAIN_FIELD);

                    // timed LRC when there is one; players that parse it show the lines along with the song
                    if (lyricsText is { } text)
                        xiph.SetField(LYRICS_FIELD, text);
                    else
                        xiph.RemoveField(LYRICS_FIELD);

                    if (artistTags is { } tags)
                        xiph.SetField(RecommendationTags.TAGS_FIELD, tags);
                    else
                        xiph.RemoveField(RecommendationTags.TAGS_FIELD);

                    if (history is { } plays)
                        xiph.SetField(RecommendationTags.HISTORY_FIELD, plays);
                    else
                        xiph.RemoveField(RecommendationTags.HISTORY_FIELD);

                    if (sound is { } features)
                        xiph.SetField(RecommendationTags.AUDIO_FIELD, features);
                    else
                        xiph.RemoveField(RecommendationTags.AUDIO_FIELD);
                }
                file.Save();
            }

            // TagLib# may have left out the end of the comment packet again
            if (OggCommentRepair.Repair(filePath))
                this.log.LogWarning("Repaired the tags of {File} after writing (TagLib# had left out their end)", track.FilePath);

            // the .lrc next to the song serves players that do not read timed lyrics from the tags
            if (lyricsText is { } timed && lyrics.IsSynced)
            {
                if (!LrcSidecar.Write(filePath, timed))
                    this.log.LogInformation("Left the user's own lyrics file next to {File} as it is", track.FilePath);
            }
            else
            {
                LrcSidecar.DeleteOwn(filePath);
            }
        });
        await repository.MarkTagsWrittenAsync(trackId);
        this.log.LogInformation("Wrote tags to {File} ({Artist} - {Title}, album {Album}, loudness {Lufs} LUFS, lyrics {Lyrics})",
            track.FilePath, track.Artist, track.Title, track.Album, loudness?.ToString("0.0", CultureInfo.InvariantCulture) ?? "unknown",
            lyricsText is null ? "none" : lyrics.IsSynced ? "timed" : "plain");
    }
}
