namespace Tapedeck.Core.Spotify;

/// <summary>
/// When a stream counts as a real play. Spotify's <c>skipped</c> flag is not used: it is always false until
/// 2022 and later also true for plays of several minutes.
/// </summary>
public sealed record SpotifyPlayRules
{
    /// <summary>At least 30 seconds (what Spotify counts as a stream), not ended with the forward or back button.</summary>
    public static readonly SpotifyPlayRules DEFAULT = new();

    public int MinMs { get; init; } = 30_000;

    /// <summary>End reasons that mean the song was not wanted, whatever the time played.</summary>
    public IReadOnlySet<string> ExcludedEndReasons { get; init; } = new HashSet<string>(StringComparer.Ordinal) { "fwdbtn", "backbtn" };

    public bool CountIncognito { get; init; } = true;

    public bool IsRealPlay(SpotifyStream stream) =>
        stream.MsPlayed >= this.MinMs
        && (stream.ReasonEnd is null || !this.ExcludedEndReasons.Contains(stream.ReasonEnd))
        && (this.CountIncognito || !stream.Incognito);

    /// <summary>The rules in a few words, for the report.</summary>
    public string Describe() =>
        $"at least {this.MinMs / 1000.0:0.#} s"
        + (this.ExcludedEndReasons.Count > 0 ? $", not ended by {string.Join(" or ", this.ExcludedEndReasons.Order(StringComparer.Ordinal))}" : string.Empty)
        + (this.CountIncognito ? string.Empty : ", not in incognito mode");
}
