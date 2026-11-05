using Tapedeck.Core.Models;

namespace Tapedeck.Core.MusicBrainz;

/// <summary>
/// What is known about a track for the MusicBrainz search, taken from the best available values:
/// user correction, YouTube Music fields (Topic uploads), the parsed title, the raw title.
/// </summary>
/// <param name="Title">Title without guests, but with qualifiers such as "(Remix)".</param>
/// <param name="Artists">Main artists; the first one is searched for.</param>
/// <param name="Guests">Artists named with "feat." in the title or the artist.</param>
/// <param name="Variants">Versions the title names, e.g. live or remix; a match must name the same.</param>
/// <param name="RawTitle">The video title as uploaded, for the fallback search.</param>
public sealed record MbQuery(
    string Title,
    IReadOnlyList<string> Artists,
    IReadOnlyList<string> Guests,
    string? Album,
    int? DurationS,
    MbVariant Variants,
    string? RawTitle,
    string? Channel)
{
    /// <returns><c>null</c> when the track has no title to search for.</returns>
    public static MbQuery? From(Track track)
    {
        var title = FirstText(track.UserTitle, track.YtTrack, track.ParsedTitle,
            track.YtTitle is { } raw ? TitleParser.Clean(raw) : null);
        if (title is null)
            return null;

        var artist = FirstText(track.UserArtist, track.YtArtist, track.ParsedArtist,
            track.Channel is { } channel ? MbNormalizer.CleanChannel(channel) : null) ?? string.Empty;

        var (titleMain, titleGuests) = MbNormalizer.SplitFeaturing(title);
        var (artistMain, artistGuests) = MbNormalizer.SplitFeaturing(artist);
        var artists = MbNormalizer.SplitArtists(artistMain);
        // "Kendrick Lamar, SZA" as the artist: SZA is a main artist, not a guest
        var guests = titleGuests.Concat(artistGuests)
            .Where(g => !artists.Contains(g, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MbQuery(
            titleMain,
            artists,
            guests,
            FirstText(track.UserAlbum, track.YtAlbum),
            track.DurationS,
            MbNormalizer.Variants(MbNormalizer.Qualifiers(titleMain)),
            FirstText(track.YtTitle),
            FirstText(track.Channel));
    }

    /// <summary>The song name without qualifiers, as searched for.</summary>
    public string CoreTitle => MbNormalizer.Core(this.Title);

    public string? MainArtist => this.Artists.Count > 0 ? this.Artists[0] : null;

    /// <summary>
    /// Recording and artist as phrases, only recordings with an official release.
    /// <c>null</c> without an artist; then only the fallback search is left.
    /// </summary>
    public string? StrictLucene => this.MainArtist is { } artist
        ? $"recording:\"{MbNormalizer.EscapePhrase(this.CoreTitle)}\" AND artist:\"{MbNormalizer.EscapePhrase(artist)}\" AND status:official"
        : null;

    /// <summary>
    /// For titles whose artist or title field is wrong (channel names, swapped parts):
    /// all words of the raw title, searched in both the recording and the artist.
    /// </summary>
    public string? FallbackLucene
    {
        get
        {
            var words = MbNormalizer.SearchWords(this.RawTitle ?? this.Title);
            if (words.Count == 0)
                return null;
            var group = string.Join(' ', words);
            return $"recording:({group}) AND artist:({group}) AND status:official";
        }
    }

    private static string? FirstText(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
