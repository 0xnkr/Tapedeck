using System.Text.Json;

namespace Tapedeck.Core.MusicBrainz;

/// <summary>Column <c>tracks.mb_candidates</c>: the best songs of the last search as JSON.</summary>
public static class MbCandidatesJson
{
    private static readonly JsonSerializerOptions OPTIONS = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Serialize(IEnumerable<MbPick> picks) => JsonSerializer.Serialize(picks.ToList(), OPTIONS);

    /// <summary>An empty or broken value gives no candidates.</summary>
    public static IReadOnlyList<MbPick> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<MbPick>>(json, OPTIONS) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
