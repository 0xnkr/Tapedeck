using Tapedeck.Core.Lyrics;
using Tapedeck.Core.Services;

namespace Tapedeck.Core.Apple;

/// <summary>
/// Writes the tags of a track into its <c>.m4a</c> copy, as Apple Music reads them. The same values as
/// <see cref="TagWriter"/> writes into the Opus file, without what only Android players use: no gain
/// (Apple Music measures its own Sound Check), no recommendation fields, no <c>.lrc</c> next to the file.
/// Never marks the track's tags as written; that is about the library file.
/// </summary>
public static class Mp4TagWriter
{
    public static void Write(string filePath, TrackTagData data)
    {
        var track = data.Track;
        using var file = TagLib.File.Create(filePath);
        var tag = file.Tag;
        tag.Title = track.Title;
        tag.Performers = string.IsNullOrEmpty(track.Artist) ? [] : [track.Artist];
        tag.AlbumArtists = string.IsNullOrEmpty(track.AlbumArtist) ? [] : [track.AlbumArtist];
        tag.Album = track.Album;
        tag.Year = (uint)(track.Year ?? 0);
        tag.Genres = track.MainGenreList.Count > 0 ? [.. track.MainGenreList] : [.. track.GenreList];
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

        // Apple Music shows the lyrics as they are, so timed LRC would show its time stamps
        tag.Lyrics = data.LyricsText is { } text
            ? data.LyricsSynced ? LrcText.ToPlain(text) : text
            : null;
        file.Save();
    }
}
