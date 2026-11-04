using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Tapedeck.Core;

/// <summary>Extracts the 11-character YouTube video ID from the common link formats.</summary>
public static partial class VideoId
{
    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex IdPattern();

    public static bool TryParse(string? input, [NotNullWhen(true)] out string? videoId)
    {
        videoId = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var text = input.Trim();
        if (IdPattern().IsMatch(text))
        {
            videoId = text;
            return true;
        }

        if (!text.Contains("://"))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? candidate = null;

        if (host is "youtu.be" or "www.youtu.be")
        {
            candidate = segments.FirstOrDefault();
        }
        else if (IsYoutubeHost(host))
        {
            if (segments is ["watch", ..])
                candidate = QueryValue(uri, "v");
            else if (segments is ["shorts" or "embed" or "live" or "v", var id, ..])
                candidate = id;
        }

        if (candidate is null || !IdPattern().IsMatch(candidate))
            return false;

        videoId = candidate;
        return true;
    }

    public static string WatchUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    internal static bool IsYoutubeHost(string host) =>
        host is "youtube.com" or "youtube-nocookie.com"
        || host.EndsWith(".youtube.com", StringComparison.Ordinal)
        || host.EndsWith(".youtube-nocookie.com", StringComparison.Ordinal);

    internal static string? QueryValue(Uri uri, string key)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == key)
                return Uri.UnescapeDataString(pair[1]);
        }
        return null;
    }
}
