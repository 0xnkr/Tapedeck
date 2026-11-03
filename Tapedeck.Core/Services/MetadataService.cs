using System.Diagnostics;
using System.Text.Json.Nodes;
using Tapedeck.Core.Models;

namespace Tapedeck.Core.Services;

/// <summary>
/// Fetches metadata via <c>yt-dlp -J</c>. yt-dlp is invoked directly instead of through YoutubeDLSharp
/// because we need the raw JSON for <c>info_json</c>.
/// </summary>
public sealed class MetadataService(ToolService tools)
{
    // would otherwise bloat info_json to several hundred KB
    private static readonly string[] DroppedKeys =
        ["formats", "requested_formats", "automatic_captions", "subtitles", "heatmap", "thumbnails"];

    public async Task<YtMetadata> FetchAsync(string url, CancellationToken ct = default)
    {
        var json = await RunYtDlpAsync(url, ct);
        return Parse(json);
    }

    public static YtMetadata Parse(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("yt-dlp did not return a JSON object.");
        foreach (var key in DroppedKeys)
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

    private async Task<string> RunYtDlpAsync(string url, CancellationToken ct)
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
        foreach (var arg in new[] { "-J", "--no-playlist", "--no-warnings", "--", url })
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
            throw new InvalidOperationException($"yt-dlp -J failed: {(await stderr).Trim()}");
        return await stdout;
    }

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    private static int? Int(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<double>(out var d) ? (int)Math.Round(d) : null;

    private static string? FirstOf(JsonObject o, string key) =>
        o[key] is JsonArray { Count: > 0 } a && a[0] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
