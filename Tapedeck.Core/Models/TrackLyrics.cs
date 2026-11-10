using Tapedeck.Core.Lyrics;

namespace Tapedeck.Core.Models;

/// <summary>What LRCLIB has for a track; all empty when it found nothing.</summary>
/// <param name="Synced">Timed LRC, only when its length fits the file.</param>
public sealed record LrclibLyrics(string? Plain, string? Synced, bool Instrumental)
{
    public static readonly LrclibLyrics NONE = new(null, null, false);

    public bool IsEmpty => this.Plain is null && this.Synced is null && !this.Instrumental;
}

/// <summary>The lyrics of a track from every source; <see cref="Text"/> is the one shown and written.</summary>
/// <param name="CheckedAt">When LRCLIB was last asked; <c>null</c> when never.</param>
public sealed record TrackLyrics(string? User, string? Synced, string? Plain, bool Instrumental, string? CheckedAt)
{
    public static readonly TrackLyrics NONE = new(null, null, null, false, null);

    /// <summary>The user's lyrics, else LRCLIB's timed ones, else its plain text.</summary>
    public string? Text => this.User ?? this.Synced ?? this.Plain;

    public bool IsSynced => this.Text is { } text && LrcText.IsSynced(text);

    public bool IsUser => this.User is not null;
}
