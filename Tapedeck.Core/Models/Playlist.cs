namespace Tapedeck.Core.Models;

/// <summary>A playlist and how many tracks it holds.</summary>
public sealed class Playlist
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int TrackCount { get; init; }

    /// <summary>The linked YouTube playlist (<c>playlist?list=…</c>), or <c>null</c> for a playlist made in the app.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>JSON of <see cref="Playlists.SmartRules"/> for a smart playlist, <c>null</c> for a normal one.</summary>
    public string? Rules { get; init; }

    /// <summary>Fills itself from the library by its rules; tracks cannot be added, removed or moved by hand.</summary>
    public bool IsSmart => this.Rules is not null;
}
