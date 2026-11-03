namespace Tapedeck.Core.Services;

/// <summary>Downloads the YouTube thumbnail (16:9) into the app data folder.</summary>
public sealed class ThumbnailService(AppPaths paths, HttpClient http)
{
    private static readonly string[] Variants = ["maxresdefault.jpg", "hqdefault.jpg"];

    /// <returns>Local path, or <c>null</c> if no thumbnail is available.</returns>
    public async Task<string?> DownloadAsync(string videoId, CancellationToken ct = default)
    {
        Directory.CreateDirectory(paths.ThumbsDir);
        var target = Path.Combine(paths.ThumbsDir, $"{videoId}.jpg");
        if (File.Exists(target))
            return target;

        foreach (var variant in Variants)
        {
            using var response = await http.GetAsync($"https://i.ytimg.com/vi/{videoId}/{variant}", ct);
            if (!response.IsSuccessStatusCode)
                continue;
            await using var file = File.Create(target);
            await response.Content.CopyToAsync(file, ct);
            return target;
        }

        return null;
    }
}
