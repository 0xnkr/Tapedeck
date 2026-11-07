namespace Tapedeck.Core.Spotify;

/// <summary>
/// Which songs of the history to take, e.g. the 200 most played or all with at least 10 real plays.
/// The period is applied before counting, so <c>Year(2019, top: 50)</c> are the 50 most played songs of 2019.
/// Songs without a real play in the period are never taken.
/// </summary>
/// <param name="From">First day, inclusive (UTC).</param>
/// <param name="To">Last day, inclusive (UTC).</param>
public sealed record SpotifySelection(int? Top = null, int? MinRealPlays = null, DateOnly? From = null, DateOnly? To = null)
{
    public static SpotifySelection Year(int year, int? top = null, int? minRealPlays = null) =>
        new(top, minRealPlays, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));

    public IReadOnlyList<SpotifySong> Apply(IEnumerable<SpotifyStream> streams, SpotifyPlayRules? rules = null)
    {
        IEnumerable<SpotifySong> songs = SpotifyAnalyzer.Analyze(streams.Where(this.Contains), rules)
            .Where(s => s.RealPlays > 0);
        if (this.MinRealPlays is { } min)
            songs = songs.Where(s => s.RealPlays >= min);
        if (this.Top is { } top)
            songs = songs.Take(top);
        return songs.ToList();
    }

    private bool Contains(SpotifyStream stream)
    {
        var day = DateOnly.FromDateTime(stream.EndedAt.UtcDateTime);
        return (this.From is not { } from || day >= from) && (this.To is not { } to || day <= to);
    }
}
