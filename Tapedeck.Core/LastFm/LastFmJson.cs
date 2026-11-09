using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tapedeck.Core.LastFm;

/// <summary>A tag of an artist and how strongly listeners gave it, 100 for the strongest.</summary>
public sealed record LastFmTag(string Name, int Count);

/// <summary>An error Last.fm answered with instead of data, e.g. 6 for an unknown artist or 10 for an invalid key.</summary>
public sealed class LastFmException(int code, string message) : Exception($"Last.fm error {code}: {message}")
{
    public const int NOT_FOUND = 6;
    public const int INVALID_KEY = 10;
    public const int TEMPORARILY_UNAVAILABLE = 16;
    public const int RATE_LIMIT = 29;

    public int Code { get; } = code;
}

/// <summary>Reads Last.fm's JSON answers.</summary>
public static class LastFmJson
{
    /// <summary>The tags of <c>artist.getTopTags</c>, strongest first.</summary>
    /// <exception cref="LastFmException">Last.fm answered with an error.</exception>
    public static IReadOnlyList<LastFmTag> ParseTopTags(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("Last.fm did not return a JSON object.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // an HTML error page, or JSON that is no object; fails this artist, not the whole run
            throw new InvalidDataException("Last.fm did not return a JSON object.", ex);
        }
        if (root["error"] is JsonValue code && code.TryGetValue<int>(out var number))
            throw new LastFmException(number, (string?)root["message"] ?? string.Empty);

        // a single tag comes as an object instead of an array
        var tags = root["toptags"]?["tag"] switch
        {
            JsonArray array => array.OfType<JsonObject>().ToList(),
            JsonObject single => [single],
            _ => [],
        };
        return tags
            .Select(t => new LastFmTag(((string?)t["name"])?.Trim() ?? string.Empty, Count(t["count"])))
            .Where(t => t.Name.Length > 0)
            .OrderByDescending(t => t.Count)
            .ToList();
    }

    // counts come as numbers or as strings, depending on the method
    private static int Count(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<int>(out var i) => i,
        JsonValue v when v.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed) => parsed,
        _ => 0,
    };
}
