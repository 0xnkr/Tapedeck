using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Tapedeck.Core.Models;

namespace Tapedeck.Core.Services;

/// <summary>
/// Fetches metadata via <c>yt-dlp -J</c>. yt-dlp is invoked directly instead of through YoutubeDLSharp
/// because we need the raw JSON for <c>info_json</c>.
/// </summary>
public sealed class MetadataService(ToolService tools, ILogger<MetadataService>? logger = null) : IYtSearch
{
    private readonly ILogger log = (ILogger?)logger ?? NullLogger.Instance;

    // would otherwise bloat info_json to several hundred KB
    private static readonly string[] DROPPED_KEYS =
        ["formats", "requested_formats", "automatic_captions", "subtitles", "heatmap", "thumbnails"];

    // placeholders yt-dlp lists for videos that cannot be played anymore
    private static readonly string[] UNAVAILABLE_TITLES = ["[Private video]", "[Deleted video]"];

    // what yt-dlp reports when YouTube rate-limits or suspects a bot; waiting helps, retrying right away does not
    private const int DESCRIPTION_LENGTH = 300;

    private static readonly string[] THROTTLE_MARKS = ["HTTP Error 429", "Too Many Requests", "Sign in to confirm"];

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

    /// <summary>The first <paramref name="count"/> videos YouTube finds for <paramref name="query"/>, without fetching each video.</summary>
    /// <exception cref="YtThrottledException">YouTube asks to slow down or to sign in.</exception>
    public async Task<IReadOnlyList<YtSearchResult>> SearchAsync(string query, int count, CancellationToken ct = default)
    {
        var json = await this.RunYtDlpAsync(["--flat-playlist", "--", $"ytsearch{count}:{query}"], ct);
        return ParseSearch(json);
    }

    public async Task<IReadOnlyList<YtSearchResult>> SearchMusicAsync(string query, int count, CancellationToken ct = default)
    {
        var url = $"https://music.youtube.com/search?q={Uri.EscapeDataString(query)}#songs";
        var json = await this.RunYtDlpAsync(["--flat-playlist", "--playlist-end", count.ToString(CultureInfo.InvariantCulture), "--", url], ct);
        return ParseSearch(json);
    }

    public async Task<YtSearchResult> FetchResultAsync(string videoId, CancellationToken ct = default)
    {
        var json = await this.RunYtDlpAsync(["--no-playlist", "--", VideoId.WatchUrl(videoId)], ct);
        return ParseResult(json);
    }

    /// <summary>The search result fields of a full video JSON; the description is cut, it is only needed for its start.</summary>
    public static YtSearchResult ParseResult(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("yt-dlp did not return a JSON object.");
        var description = Str(root, "description");
        return new YtSearchResult(
            VideoId: Str(root, "id") ?? throw new InvalidDataException("yt-dlp did not return a video ID."),
            Title: Str(root, "title") ?? string.Empty,
            Channel: Str(root, "channel") ?? Str(root, "uploader"),
            DurationS: Int(root, "duration"),
            ViewCount: root["view_count"] is JsonValue v && v.TryGetValue<double>(out var views) ? (long)views : null,
            Description: description is { Length: > DESCRIPTION_LENGTH } ? description[..DESCRIPTION_LENGTH] : description,
            Artists: root["artists"] is JsonArray artists
                ? artists.Select(a => a?.GetValue<string>()).OfType<string>().ToList()
                : Str(root, "artist") is { } artist ? [artist] : null);
    }

    /// <summary>Maps the flat search JSON; entries that are no video (channels, playlists) are left out.</summary>
    public static IReadOnlyList<YtSearchResult> ParseSearch(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("yt-dlp did not return a JSON object.");
        var results = new List<YtSearchResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (root["entries"] is not JsonArray items)
            return results;

        foreach (var item in items.OfType<JsonObject>())
        {
            var title = Str(item, "title");
            if (!VideoId.TryParse(Str(item, "id"), out var videoId) || title is null || UNAVAILABLE_TITLES.Contains(title) || !seen.Add(videoId))
                continue;
            results.Add(new YtSearchResult(
                VideoId: videoId,
                Title: title,
                Channel: Str(item, "channel") ?? Str(item, "uploader"),
                DurationS: Int(item, "duration"),
                ViewCount: item["view_count"] is JsonValue v && v.TryGetValue<double>(out var views) ? (long)views : null,
                Description: Str(item, "description")));
        }
        return results;
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
            if (THROTTLE_MARKS.Any(m => error.Contains(m, StringComparison.OrdinalIgnoreCase)))
                throw new YtThrottledException(error);
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
