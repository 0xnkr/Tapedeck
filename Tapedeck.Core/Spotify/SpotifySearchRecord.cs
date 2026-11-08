using Tapedeck.Core.Models;

namespace Tapedeck.Core.Spotify;

/// <summary>A stored YouTube search for a song, with the user's decision.</summary>
/// <param name="Decision"><c>null</c>, <see cref="SpotifyMatchState.Accepted"/> or <see cref="SpotifyMatchState.Rejected"/>.</param>
/// <param name="VideoId">The video the user chose or the song was imported from.</param>
public sealed record SpotifySearchRecord(
    string SongKey,
    string Query,
    IReadOnlyList<YtSearchResult> Results,
    DateTimeOffset SearchedAt,
    SpotifyMatchState? Decision,
    string? VideoId,
    long? TrackId)
{
    /// <summary>The state of the song: the user's decision, otherwise the matcher's judgement of the stored results.</summary>
    public SpotifyMatchState State(SpotifySong song) => this.Decision ?? SpotifyYtMatcher.Match(song, this.Results).State;

    /// <summary>The video to import: the chosen one, or the sure match. <c>null</c> when unsure, rejected or not found.</summary>
    public string? VideoToImport(SpotifySong song) => this.Decision switch
    {
        SpotifyMatchState.Accepted => this.VideoId,
        SpotifyMatchState.Rejected => null,
        _ => SpotifyYtMatcher.Match(song, this.Results) is { State: SpotifyMatchState.Sure, Best: { } best } ? best.Video.VideoId : null,
    };
}
