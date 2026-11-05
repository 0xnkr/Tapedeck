namespace Tapedeck.Core.Models;

/// <summary>State of the MusicBrainz match, column <c>tracks.mb_status</c>.</summary>
public enum MbMatchState
{
    /// <summary>Not searched yet.</summary>
    None,
    /// <summary>A match is applied, the <c>mb_*</c> columns are filled.</summary>
    Matched,
    /// <summary>Candidates were found but none is certain; the user confirms one on the detail page.</summary>
    Suggested,
    /// <summary>Searched, nothing usable found.</summary>
    NoMatch,
    /// <summary>The user removed a match or said the track is not on MusicBrainz; bulk runs skip it.</summary>
    Rejected,
}

public static class MbMatchStateExtensions
{
    public static string ToDb(this MbMatchState state) => state switch
    {
        MbMatchState.Matched => "matched",
        MbMatchState.Suggested => "suggested",
        MbMatchState.NoMatch => "no_match",
        MbMatchState.Rejected => "rejected",
        _ => "none",
    };

    public static MbMatchState ParseDb(string? value) => value switch
    {
        "matched" => MbMatchState.Matched,
        "suggested" => MbMatchState.Suggested,
        "no_match" => MbMatchState.NoMatch,
        "rejected" => MbMatchState.Rejected,
        _ => MbMatchState.None,
    };
}
