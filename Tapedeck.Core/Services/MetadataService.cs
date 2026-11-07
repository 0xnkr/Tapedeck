using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Tapedeck.Core.Models;

namespace Tapedeck.Core.Services;

/// <summary>
/// Fetches metadata via <c>yt-dlp -J</c>. yt-dlp is invoked directly instead of through YoutubeDLSharp
/// because we need the raw JSON for <c>info_json</c>.
/// </summary>
public sealed class MetadataService(ToolService tools, ILogger<MetadataService>? logger = null)
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    // would otherwise bloat info_json to several hundred KB
    private static readonly string[] DROPPED_KEYS =
        ["formats", "requested_formats", "automatic_captions", "subtitles", "heatmap", "thumbnails"];

    // placeholders yt-dlp lists for videos that cannot be played anymore
    private static readonly string[] UNAVAILABLE_TITLES = ["[Private video]", "[Deleted video]"];

    public async Task<YtMetadata> FetchAsync(string url, CancellationToken ct = default)
    {
        var json = await this.RunYtDlpAsync(["--no-playlist", "--", url], ct);
        return Parse(json);
    }

    /// <summary>Reads the videos of a playlist without fetching each video, which takes seconds instead of minutes.</summary>
    public async Task<YtPlaylist> FetchPlaylistAsync(string listId, CancellationToken ct = default)
    {
        var json = await this.RunYtDlpAsync(["--flat-playlist", "--yes-playlist", "--", PlaylistLink.Url(listId)], ct);
        return ParsePlaylist(json);
    }

    public static YtMetadata Parse(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("yt-dlp did not return a JSON object.");
        foreach (var key in DROPPED_KEYS)
            root.Remove(key);

        var title = Str(root, "title");
        var (parsedArtist, parsedTitle) = title is null ? (null, null) : TitleParser.Parse(title);

        return new YtMetadata(
            VideoId: Str(root, "id") ?? throw new InvalidDataException("yt-dlp did not return a video ID."),
            Title: title,
            Channel: Str(root, "channel") ?? Str(root, "uploader"),
            UploadDate: Str(root, "upload_date"),
            DurationS: Int(root, "duration"),
            Track: Str(root, "track"),
            Artist: FirstOf(root, "artists") ?? Str(root, "artist"),
            Album: Str(root, "album"),
            ReleaseYear: Int(root, "release_year"),
            ParsedArtist: parsedArtist,
            ParsedTitle: parsedTitle,
            Categories: root["categories"] is JsonArray cats
                ? cats.Select(c => c?.GetValue<string>()).OfType<string>().ToList()
                : [],
            InfoJson: root.ToJsonString());
    }

    /// <summary>Maps the flat playlist JSON; unavailable and repeated videos are left out.</summary>
    public static YtPlaylist ParsePlaylist(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("yt-dlp did not return a JSON object.");
        var entries = new List<YtPlaylistEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (root["entries"] is JsonArray items)
        {
            foreach (var item in items.OfType<JsonObject>())
            {
                var title = Str(item, "title");
                if (!VideoId.TryParse(Str(item, "id"), out var videoId) || (title is not null && UNAVAILABLE_TITLES.Contains(title)) || !seen.Add(videoId))
                    continue;
                entries.Add(new YtPlaylistEntry(videoId, title));
            }
        }

        return new YtPlaylist(
            Id: Str(root, "id") ?? throw new InvalidDataException("yt-dlp did not return a playlist ID."),
            Title: Str(root, "title"),
            Channel: Str(root, "channel") ?? Str(root, "uploader"),
            Entries: entries);
    }

    private async Task<string> RunYtDlpAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(tools.YtDlpPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in tools.JsRuntimeArgs)
            psi.ArgumentList.Add(arg);
        foreach (var arg in (string[])["-J", "--no-warnings", .. args])
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start yt-dlp.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0)
        {
            var error = (await stderr).Trim();
            this.log.LogWarning("yt-dlp -J {Arguments} failed with exit code {ExitCode}: {Error}",
                string.Join(' ', args), process.ExitCode, error);
            throw new InvalidOperationException($"yt-dlp -J failed: {error}");
        }
        return await stdout;
    }

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    private static int? Int(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<double>(out var d) ? (int)Math.Round(d) : null;

    private static string? FirstOf(JsonObject o, string key) =>
        o[key] is JsonArray { Count: > 0 } a && a[0] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
