using Tapedeck.Core.Models;

namespace Tapedeck.Core.Services;

/// <summary>Add a link: create the track, fetch metadata via yt-dlp, download the thumbnail.</summary>
public sealed class TrackImportService(ITrackRepository repository, MetadataService metadata, ThumbnailService thumbnails)
{
    public async Task<long> AddAsync(string link, CancellationToken ct = default)
    {
        if (!VideoId.TryParse(link, out var videoId))
            throw new ArgumentException($"Not a valid YouTube link: {link}", nameof(link));

        var id = await repository.AddAsync(videoId, VideoId.WatchUrl(videoId));
        var meta = await metadata.FetchAsync(VideoId.WatchUrl(videoId), ct);
        await repository.UpdateYtMetadataAsync(id, meta);

        // YouTube practically only returns "Music" as a category, which is not worth a tag
        foreach (var category in meta.Categories.Where(c => !c.Equals("Music", StringComparison.OrdinalIgnoreCase)))
            await repository.AddTagAsync(id, category, TagSource.Yt);

        if (await thumbnails.DownloadAsync(videoId, ct) is { } thumb)
            await repository.SetThumbPathAsync(id, thumb);

        return id;
    }
}
