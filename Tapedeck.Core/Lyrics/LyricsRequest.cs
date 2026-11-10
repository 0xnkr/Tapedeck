namespace Tapedeck.Core.Lyrics;

/// <summary>A track as LRCLIB is asked for it: the shown names and the length of the file.</summary>
/// <param name="Album">Helps LRCLIB tell the album version from others; <c>null</c> when unknown.</param>
/// <param name="DurationS">Length of the video; timed lyrics are only taken when theirs fits.</param>
/// <param name="IsTopic">
/// A YouTube Music upload ("Provided to YouTube by …"): the plain release, whose length alone says the timing
/// fits. Other videos may be as long as a live recording by chance.
/// </param>
public sealed record LyricsRequest(long Id, string Artist, string Title, string? Album, int? DurationS, bool IsTopic = false);
