using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tapedeck.Core.Models;

namespace Tapedeck.Core.Services;

/// <summary>
/// Add a link: create the track, fetch metadata via yt-dlp, download the thumbnail.
/// The video of a merged duplicate leads to the track it was merged into, without fetching anything.
/// </summary>
public sealed class TrackImportService(
    ITrackRepository repository,
    MetadataService metadata,
    ThumbnailService thumbnails,
    ILogger<TrackImportService>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    public async Task<long> AddAsync(string link, CancellationToken ct = default)
    {
        if (!VideoId.TryParse(link, out var videoId))
            throw new ArgumentException($"Not a valid YouTube link: {link}", nameof(link));

        if (await repository.GetAliasTargetAsync(videoId) is { } kept)
        {
            this.log.LogInformation("Video {VideoId} was merged into track {TrackId}, nothing to add", videoId, kept);
            return kept;
        }

        var id = await repository.AddAsync(videoId, VideoId.WatchUrl(videoId));
        var meta = await metadata.FetchAsync(VideoId.WatchUrl(videoId), ct);
        await repository.UpdateYtMetadataAsync(id, meta);
        this.log.LogInformation("Added {VideoId}: {Title} ({Channel})", videoId, meta.Title, meta.Channel);

        // YouTube practically only returns "Music" as a category, which is not worth a tag
        foreach (var category in meta.Categories.Where(c => !c.Equals("Music", StringComparison.OrdinalIgnoreCase)))
            await repository.AddTagAsync(id, category, TagSource.Yt);

        if (await thumbnails.DownloadAsync(videoId, ct) is { } thumb)
            await repository.SetThumbPathAsync(id, thumb);

        return id;
    }
}
