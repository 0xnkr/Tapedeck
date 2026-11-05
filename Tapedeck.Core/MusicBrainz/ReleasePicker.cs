namespace Tapedeck.Core.MusicBrainz;

/// <summary>
/// Picks the release for a recognized song by rules, not by score: the studio album before soundtrack,
/// EP, single and compilation, the earliest release group, then a worldwide or US edition.
/// The duration never decides here, so a music video gets the album it belongs to.
/// </summary>
public static class ReleasePicker
{
    private const string NO_DATE = "9999";

    /// <param name="recordings">All recordings of one song, variants already removed.</param>
    /// <returns><c>null</c> when none of the recordings has a release.</returns>
    public static MbPick? Pick(IReadOnlyList<MbRecording> recordings, double score)
    {
        var pairs = recordings.SelectMany(r => r.Releases.Select(rel => (Recording: r, Release: rel))).ToList();
        if (pairs.Count == 0)
            return null;

        // bootlegs and promos only when nothing official is left
        var official = pairs.Where(p => string.Equals(p.Release.Status, "Official", StringComparison.OrdinalIgnoreCase)).ToList();
        if (official.Count > 0)
            pairs = official;

        // first release of each release group, as far as these releases tell
        var groupDates = pairs
            .Where(p => p.Release.Date is not null)
            .GroupBy(p => p.Release.ReleaseGroup.Id)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Release.Date!).Min(StringComparer.Ordinal)!);

        var songArtists = recordings.SelectMany(r => r.Artists).Select(a => a.ArtistId).ToHashSet();
        var best = pairs
            .OrderBy(p => Tier(p.Release, songArtists))
            .ThenBy(p => groupDates.GetValueOrDefault(p.Release.ReleaseGroup.Id, NO_DATE), StringComparer.Ordinal)
            .ThenBy(p => p.Release.ReleaseGroup.Id, StringComparer.Ordinal)
            // the plain edition, named like the album, before "(Deluxe)" and the like
            .ThenBy(p => IsPlainEdition(p.Release) ? 0 : 1)
            .ThenBy(p => CountryRank(p.Release.Country))
            .ThenBy(p => p.Release.Date ?? NO_DATE, StringComparer.Ordinal)
            .ThenBy(p => VersionRank(p.Recording.Disambiguation))
            .ThenBy(p => p.Release.Id, StringComparer.Ordinal)
            .First();

        var (recording, release) = best;
        var main = recording.MainArtist;
        return new MbPick(
            recording.Id,
            recording.Title,
            recording.ArtistText,
            main?.ArtistId ?? string.Empty,
            release.Id,
            release.ReleaseGroup.Id,
            release.Title,
            release.Artists.Count > 0 ? MbArtistCredits.Join(release.Artists) : main?.Name ?? recording.ArtistText,
            groupDates.GetValueOrDefault(release.ReleaseGroup.Id),
            release.TrackNumber,
            release.DiscNumber,
            recording.LengthMs is { } ms ? (int)Math.Round(ms / 1000.0) : null,
            Math.Round(score, 3));
    }

    /// <summary>
    /// 0 studio album · 1 soundtrack album · 2 EP · 3 single · 4 compilation album · 5 everything else.
    /// A song that came out on a soundtrack first ("Gangsta's Paradise") still gets the artist's own album.
    /// </summary>
    public static int Tier(MbReleaseGroup group)
    {
        var plain = group.SecondaryTypes.Count == 0;
        var soundtrack = !plain && group.SecondaryTypes.All(s => s.Equals("Soundtrack", StringComparison.OrdinalIgnoreCase));
        var compilation = !plain && group.SecondaryTypes.All(s => s.Equals("Compilation", StringComparison.OrdinalIgnoreCase)
                                                                  || s.Equals("Soundtrack", StringComparison.OrdinalIgnoreCase));
        return group.PrimaryType?.ToLowerInvariant() switch
        {
            "album" when plain => 0,
            "album" when soundtrack => 1,
            "ep" when plain || soundtrack => 2,
            "single" when plain || soundtrack => 3,
            "album" when compilation => 4,
            _ => 5,
        };
    }

    /// <summary>
    /// <see cref="Tier(MbReleaseGroup)"/>, but a plain album none of the song's artists is credited for
    /// counts as a compilation: various-artists collections are often entered without that type.
    /// </summary>
    public static int Tier(MbRelease release, IReadOnlySet<string> songArtists)
    {
        var tier = Tier(release.ReleaseGroup);
        var foreign = release.Artists.Count > 0 && !release.Artists.Any(a => songArtists.Contains(a.ArtistId));
        return tier == 0 && foreign ? 4 : tier;
    }

    private static bool IsPlainEdition(MbRelease release) =>
        release.ReleaseGroup.Title is not { } album
        || MbNormalizer.Normalize(release.Title) == MbNormalizer.Normalize(album);

    private static int CountryRank(string? country) => country switch
    {
        "XW" => 0,
        "US" => 1,
        _ => 2,
    };

    /// <summary>The explicit album version first, as on the album; clean and remixes for Dolby Atmos after.</summary>
    private static int VersionRank(string? disambiguation)
    {
        var text = MbNormalizer.Normalize(disambiguation);
        if (text.Contains("atmos", StringComparison.Ordinal) || text.Contains("spatial", StringComparison.Ordinal))
            return 3;
        if (MbNormalizer.ContainsWords(text, "clean"))
            return 2;
        return MbNormalizer.ContainsWords(text, "explicit") ? 0 : 1;
    }
}
