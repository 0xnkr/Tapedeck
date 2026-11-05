namespace Tapedeck.Core.Services;

/// <summary>
/// Downloads the square front cover of a release from the Cover Art Archive into the app data folder.
/// Tracks of the same release share the file.
/// </summary>
public sealed class CoverArtService(AppPaths paths, HttpClient http)
{
    private const string BASE_URL = "https://coverartarchive.org/";

    /// <summary>500 px is plenty for a phone and keeps the files small; tagged into every track of the album.</summary>
    private const string SIZE = "front-500";

    /// <returns>Local path, or <c>null</c> when neither the release nor its release group has a front cover.</returns>
    public async Task<string?> DownloadAsync(string releaseId, string? releaseGroupId, CancellationToken ct = default)
    {
        Directory.CreateDirectory(paths.CoversDir);
        var target = Path.Combine(paths.CoversDir, $"{releaseId}.jpg");
        if (File.Exists(target))
            return target;

        var sources = new List<string> { $"{BASE_URL}release/{releaseId}/{SIZE}" };
        // another edition of the album often has the cover this one lacks
        if (!string.IsNullOrEmpty(releaseGroupId))
            sources.Add($"{BASE_URL}release-group/{releaseGroupId}/{SIZE}");

        foreach (var url in sources)
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                continue;

            // write to a temp file first, so a cancelled download never leaves half an image behind
            var temp = target + ".tmp";
            await using (var file = File.Create(temp))
                await response.Content.CopyToAsync(file, ct);
            File.Move(temp, target, overwrite: true);
            return target;
        }

        return null;
    }
}
