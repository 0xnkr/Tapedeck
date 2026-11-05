using System.Globalization;
using System.Text.Json;

namespace Tapedeck.Core.MusicBrainz;

/// <summary>Reads the JSON of the MusicBrainz web service into <see cref="MbRecording"/>s.</summary>
public static class MbJson
{
    /// <summary>Recordings of a <c>/ws/2/recording?query=…</c> response.</summary>
    public static IReadOnlyList<MbRecording> ParseRecordingSearch(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("recordings", out var recordings))
            return [];
        return recordings.EnumerateArray().Select(ParseRecording).ToList();
    }

    /// <summary>
    /// Genres of a <c>/ws/2/release-group/{id}?inc=genres</c> response, most votes first.
    /// </summary>
    public static IReadOnlyList<string> ParseGenres(string json, int max)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("genres", out var genres))
            return [];
        return genres.EnumerateArray()
            .Select(g => (Name: Text(g, "name"), Count: Int(g, "count") ?? 0))
            .Where(g => !string.IsNullOrWhiteSpace(g.Name) && g.Count > 0)
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .Take(max)
            .Select(g => g.Name!)
            .ToList();
    }

    private static MbRecording ParseRecording(JsonElement element) => new(
        Text(element, "id") ?? string.Empty,
        Text(element, "title") ?? string.Empty,
        NullIfEmpty(Text(element, "disambiguation")),
        Int(element, "length"),
        ParseCredits(element),
        element.TryGetProperty("releases", out var releases)
            ? releases.EnumerateArray().Select(ParseRelease).ToList()
            : []);

    private static MbRelease ParseRelease(JsonElement element)
    {
        var group = element.TryGetProperty("release-group", out var g)
            ? new MbReleaseGroup(
                Text(g, "id") ?? string.Empty,
                Text(g, "title"),
                Text(g, "primary-type"),
                g.TryGetProperty("secondary-types", out var secondary)
                    ? secondary.EnumerateArray().Select(s => s.GetString() ?? string.Empty).Where(s => s.Length > 0).ToList()
                    : [])
            : new MbReleaseGroup(string.Empty, null, null, []);

        int? track = null;
        int? disc = null;
        if (element.TryGetProperty("media", out var media) && media.GetArrayLength() > 0)
        {
            var medium = media[0];
            disc = Int(medium, "position");
            track = Int(medium, "track-offset") is { } offset
                ? offset + 1
                : medium.TryGetProperty("track", out var tracks) && tracks.GetArrayLength() > 0
                  && int.TryParse(Text(tracks[0], "number"), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : null;
        }

        return new MbRelease(
            Text(element, "id") ?? string.Empty,
            Text(element, "title") ?? string.Empty,
            Text(element, "status"),
            NullIfEmpty(Text(element, "date")),
            NullIfEmpty(Text(element, "country")),
            group,
            ParseCredits(element),
            track,
            disc);
    }

    private static IReadOnlyList<MbArtistCredit> ParseCredits(JsonElement element)
    {
        if (!element.TryGetProperty("artist-credit", out var credits))
            return [];
        return credits.EnumerateArray().Select(c =>
        {
            var artist = c.TryGetProperty("artist", out var a) ? a : default;
            var artistName = artist.ValueKind == JsonValueKind.Object ? Text(artist, "name") : null;
            var name = Text(c, "name") ?? artistName ?? string.Empty;
            return new MbArtistCredit(
                artist.ValueKind == JsonValueKind.Object ? Text(artist, "id") ?? string.Empty : string.Empty,
                name,
                artistName ?? name,
                Text(c, "joinphrase") ?? string.Empty);
        }).ToList();
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
