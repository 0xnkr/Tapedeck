using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tapedeck.Core.Lyrics;

/// <summary>One lyrics record of LRCLIB.</summary>
/// <param name="DurationS">Length of the recording the lyrics were timed to; <c>null</c> for some records.</param>
/// <param name="Synced">LRC with a time per line; <c>null</c> when only the plain text is known.</param>
public sealed record LrclibRecord(
    long Id, string Title, string Artist, string? Album, double? DurationS, bool Instrumental, string? Plain, string? Synced);

/// <summary>Reads LRCLIB's JSON answers.</summary>
public static class LrclibJson
{
    /// <summary>The record of <c>/api/get</c>.</summary>
    /// <exception cref="InvalidDataException">No JSON object, e.g. an HTML error page.</exception>
    public static LrclibRecord ParseRecord(string json) =>
        Record(Parse(json) as JsonObject ?? throw new InvalidDataException("LRCLIB did not return a JSON object."));

    /// <summary>The records of <c>/api/search</c>.</summary>
    /// <exception cref="InvalidDataException">No JSON array, e.g. an HTML error page.</exception>
    public static IReadOnlyList<LrclibRecord> ParseSearch(string json) =>
        (Parse(json) as JsonArray ?? throw new InvalidDataException("LRCLIB did not return a JSON array."))
        .OfType<JsonObject>()
        .Select(Record)
        .ToList();

    private static JsonNode? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("LRCLIB did not return JSON.", ex);
        }
    }

    private static LrclibRecord Record(JsonObject o) => new(
        Id: o["id"] is JsonValue id && id.TryGetValue<long>(out var number) ? number : 0,
        Title: Str(o, "trackName") ?? Str(o, "name") ?? string.Empty,
        Artist: Str(o, "artistName") ?? string.Empty,
        Album: Str(o, "albumName"),
        DurationS: o["duration"] is JsonValue d && d.TryGetValue<double>(out var seconds) ? seconds : null,
        Instrumental: o["instrumental"] is JsonValue i && i.TryGetValue<bool>(out var instrumental) && instrumental,
        Plain: Str(o, "plainLyrics"),
        Synced: Str(o, "syncedLyrics"));

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
}
