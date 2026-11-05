namespace Tapedeck.Core.MusicBrainz;

/// <summary>One artist of an artist credit, e.g. "Kendrick Lamar" with join phrase " &amp; " before "SZA".</summary>
/// <param name="Name">Name as credited on this recording or release.</param>
/// <param name="ArtistName">The artist's own name, which can differ from the credited one.</param>
public sealed record MbArtistCredit(string ArtistId, string Name, string ArtistName, string JoinPhrase);

/// <param name="Title">The album's own name, without "(Deluxe)" and the like of single editions.</param>
public sealed record MbReleaseGroup(string Id, string? Title, string? PrimaryType, IReadOnlyList<string> SecondaryTypes);

/// <param name="Artists">Artist credit of the release; empty when it equals the recording's.</param>
/// <param name="TrackNumber">Position of the recording on its medium (1-based).</param>
/// <param name="DiscNumber">Position of that medium in the release (1-based).</param>
public sealed record MbRelease(
    string Id,
    string Title,
    string? Status,
    string? Date,
    string? Country,
    MbReleaseGroup ReleaseGroup,
    IReadOnlyList<MbArtistCredit> Artists,
    int? TrackNumber,
    int? DiscNumber);

/// <summary>A recording from the MusicBrainz search, reduced to what the matching needs.</summary>
public sealed record MbRecording(
    string Id,
    string Title,
    string? Disambiguation,
    int? LengthMs,
    IReadOnlyList<MbArtistCredit> Artists,
    IReadOnlyList<MbRelease> Releases)
{
    public string ArtistText => MbArtistCredits.Join(this.Artists);

    public MbArtistCredit? MainArtist => this.Artists.Count > 0 ? this.Artists[0] : null;
}

public static class MbArtistCredits
{
    /// <summary>"Kendrick Lamar &amp; SZA": the credited names with their join phrases.</summary>
    public static string Join(IReadOnlyList<MbArtistCredit> credits) =>
        string.Concat(credits.Select(c => c.Name + c.JoinPhrase)).Trim();
}

/// <summary>
/// The values a match puts on a track: the recording, the chosen release and its place on it.
/// Also stored as JSON for the candidates the user can pick from.
/// </summary>
/// <param name="Date">First release date of the release group (<c>YYYY</c>, <c>YYYY-MM</c> or <c>YYYY-MM-DD</c>).</param>
/// <param name="Score">How well the song matches, 0..1.</param>
public sealed record MbPick(
    string RecordingId,
    string Title,
    string Artist,
    string ArtistId,
    string ReleaseId,
    string ReleaseGroupId,
    string Album,
    string AlbumArtist,
    string? Date,
    int? TrackNumber,
    int? DiscNumber,
    int? LengthS,
    double Score);
