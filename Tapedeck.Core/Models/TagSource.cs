namespace Tapedeck.Core.Models;

/// <summary>Origin of a genre tag, column <c>track_tags.source</c>.</summary>
public enum TagSource
{
    Yt,
    Lastfm,
    Mb,
    User,
}

public static class TagSourceExtensions
{
    public static string ToDb(this TagSource source) => source.ToString().ToLowerInvariant();
}
