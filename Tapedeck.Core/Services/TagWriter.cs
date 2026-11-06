namespace Tapedeck.Core.Services;

/// <summary>
/// Writes the values from <c>tracks_display</c> into the audio file. Android players sort by these tags,
/// not by folders. Repeatable: existing tags are overwritten, not appended.
/// </summary>
public sealed class TagWriter(ITrackRepository repository, AppPaths paths)
{
    public async Task WriteTagsAsync(long trackId)
    {
        var track = await repository.GetDisplayAsync(trackId)
                    ?? throw new InvalidOperationException($"Track {trackId} does not exist.");
        if (track.FilePath is null)
            throw new InvalidOperationException($"Track {trackId} has no file yet.");

        var filePath = paths.ToLibraryPath(track.FilePath);
        await Task.Run(() =>
        {
            // files damaged by an earlier write could not even be opened
            OggCommentRepair.Repair(filePath);
            using (var file = TagLib.File.Create(filePath))
            {
                var tag = file.Tag;
                tag.Title = track.Title;
                tag.Performers = string.IsNullOrEmpty(track.Artist) ? [] : [track.Artist];
                tag.AlbumArtists = string.IsNullOrEmpty(track.AlbumArtist) ? [] : [track.AlbumArtist];
                tag.Album = track.Album;
                tag.Year = (uint)(track.Year ?? 0);
                tag.Genres = [.. track.GenreList];
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
                file.Save();
            }

            // TagLib# may have left out the end of the comment packet again
            OggCommentRepair.Repair(filePath);
        });
        await repository.MarkTagsWrittenAsync(trackId);
    }
}
