namespace Tapedeck.Core.Spotify;

/// <summary>
/// One play of a song from Spotify's extended streaming history. Only what the analysis needs is kept;
/// IP address, platform and country are never read.
/// </summary>
/// <param name="EndedAt">When the play stopped (Spotify's <c>ts</c>, UTC).</param>
/// <param name="Artist">Spotify's album artist, which may name several artists.</param>
/// <param name="ReasonStart">Why the play started, e.g. <c>clickrow</c>, <c>trackdone</c>, <c>fwdbtn</c>.</param>
/// <param name="ReasonEnd">Why it stopped, e.g. <c>trackdone</c>, <c>endplay</c>, <c>fwdbtn</c>, <c>logout</c>.</param>
public sealed record SpotifyStream(
    DateTimeOffset EndedAt,
    string TrackUri,
    string Artist,
    string Title,
    string? Album,
    int MsPlayed,
    string? ReasonStart,
    string? ReasonEnd,
    bool Incognito);

/// <summary>What reading the history skipped.</summary>
/// <param name="Rows">All rows of all files, including the skipped ones.</param>
/// <param name="Episodes">Podcast episodes, show videos and audiobook chapters.</param>
/// <param name="NoMetadata">Rows without a track, e.g. local files or songs removed from Spotify.</param>
/// <param name="Duplicates">Rows that repeat another row exactly.</param>
public sealed record SpotifyReadStats(int Files, int Rows, int Episodes, int NoMetadata, int Duplicates);

/// <summary>The song plays of a streaming history, oldest first.</summary>
public sealed record SpotifyHistory(IReadOnlyList<SpotifyStream> Streams, SpotifyReadStats Stats);
