using Tapedeck.Core.Models;

namespace Tapedeck.Core.Services;

/// <summary>YouTube searches; <see cref="MetadataService"/> runs them with yt-dlp. All may throw <see cref="YtThrottledException"/>.</summary>
public interface IYtSearch
{
    /// <summary>Videos of the YouTube search, with channel, length and the start of the description.</summary>
    Task<IReadOnlyList<YtSearchResult>> SearchAsync(string query, int count, CancellationToken ct = default);

    /// <summary>Songs of the YouTube Music search: the plain releases, but only with ID and title.</summary>
    Task<IReadOnlyList<YtSearchResult>> SearchMusicAsync(string query, int count, CancellationToken ct = default);

    /// <summary>One video with everything a search result can have, including the artists of a song.</summary>
    Task<YtSearchResult> FetchResultAsync(string videoId, CancellationToken ct = default);
}
