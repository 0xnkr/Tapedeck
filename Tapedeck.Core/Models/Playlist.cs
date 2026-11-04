namespace Tapedeck.Core.Models;

/// <summary>A playlist and how many tracks it holds.</summary>
public sealed class Playlist
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int TrackCount { get; init; }
}
