namespace Tapedeck.Core.Models;

/// <summary>A YouTube playlist as read by <c>yt-dlp --flat-playlist -J</c>: only IDs and titles, no per-video metadata.</summary>
public sealed record YtPlaylist(string Id, string? Title, string? Channel, IReadOnlyList<YtPlaylistEntry> Entries);

/// <summary>An available video of a playlist, in playlist order.</summary>
public sealed record YtPlaylistEntry(string VideoId, string? Title);
