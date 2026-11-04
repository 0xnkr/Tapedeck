using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Tapedeck.Core;

/// <summary>
/// Extracts the ID of a YouTube playlist from a link (<c>playlist?list=…</c>, or a video opened inside a playlist).
/// Mixes (<c>list=RD…</c>) are generated per viewer and change on every call, so they do not count as playlists.
/// </summary>
public static partial class PlaylistLink
{
    [GeneratedRegex("^[A-Za-z0-9_-]{2,64}$")]
    private static partial Regex IdPattern();

    /// <returns><c>true</c> when the link carries a playlist that can be imported.</returns>
    public static bool TryParse(string? input, [NotNullWhen(true)] out string? listId)
    {
        listId = null;
        if (ListIdOf(input) is not { } id || IsMix(id))
            return false;

        listId = id;
        return true;
    }

    /// <summary>The link carries a mix, which is imported as a single video only.</summary>
    public static bool IsMixLink(string? input) => ListIdOf(input) is { } id && IsMix(id);

    public static string Url(string listId) => $"https://www.youtube.com/playlist?list={listId}";

    // "RDCLAK…" are curated YouTube Music playlists, which stay the same like ordinary playlists
    private static bool IsMix(string listId) =>
        listId.StartsWith("RD", StringComparison.Ordinal) && !listId.StartsWith("RDCLAK", StringComparison.Ordinal);

    private static bool IsYoutubeOrShortHost(string host) =>
        host is "youtu.be" or "www.youtu.be" || VideoId.IsYoutubeHost(host);

    private static string? ListIdOf(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var text = input.Trim();
        if (!text.Contains("://"))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !IsYoutubeOrShortHost(uri.Host.ToLowerInvariant()))
            return null;

        var id = VideoId.QueryValue(uri, "list");
        return id is not null && IdPattern().IsMatch(id) ? id : null;
    }
}
