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
            using var file = TagLib.File.Create(filePath);
            var tag = file.Tag;
            tag.Title = track.Title;
            tag.Performers = string.IsNullOrEmpty(track.Artist) ? [] : [track.Artist];
            tag.AlbumArtists = string.IsNullOrEmpty(track.AlbumArtist) ? [] : [track.AlbumArtist];
            tag.Album = track.Album;
            tag.Year = (uint)(track.Year ?? 0);
            tag.Genres = [.. track.GenreList];
            tag.Comment = track.Url;
            tag.Pictures = track.Cover is { } cover && File.Exists(cover)
                ? [new TagLib.Picture(cover) { Type = TagLib.PictureType.FrontCover }]
                : [];
            file.Save();
        });
        await repository.MarkTagsWrittenAsync(trackId);
    }
}
