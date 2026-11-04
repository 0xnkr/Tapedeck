namespace Tapedeck.Core.Models;

/// <summary>Where a displayed value comes from, columns <c>*_source</c> of <c>tracks_display</c>.</summary>
public enum FieldSource
{
    /// <summary>No source has a value.</summary>
    None,
    /// <summary>Corrected by the user.</summary>
    User,
    /// <summary>MusicBrainz enrichment.</summary>
    Mb,
    /// <summary>Last.fm (genres only).</summary>
    Lastfm,
    /// <summary>yt-dlp metadata.</summary>
    Yt,
    /// <summary>Split from the YouTube title by <see cref="TitleParser"/>.</summary>
    Parsed,
    /// <summary>The YouTube channel name as a last resort for the artist.</summary>
    Channel,
    /// <summary>Taken from another field, e.g. the album artist from the artist.</summary>
    Derived,
}

public static class FieldSourceExtensions
{
    public static FieldSource ParseDb(string value) => Enum.Parse<FieldSource>(value, ignoreCase: true);
}
