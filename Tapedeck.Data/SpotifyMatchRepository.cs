using System.Globalization;
using System.Text.Json;
using Dapper;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.Spotify;

namespace Tapedeck.Data;

public sealed class SpotifyMatchRepository(SqliteConnectionFactory connectionFactory) : ISpotifyMatchRepository
{
    public async Task<IReadOnlyDictionary<string, SpotifySearchRecord>> ListAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<Row>(
            "SELECT song_key, query, results_json, searched_at, decision, video_id, track_id FROM spotify_matches;");
        return rows.ToDictionary(r => r.SongKey, ToRecord, StringComparer.Ordinal);
    }

    public async Task SaveSearchAsync(string songKey, string query, IReadOnlyList<YtSearchResult> results)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO spotify_matches (song_key, query, results_json) VALUES (@songKey, @query, @json)
            ON CONFLICT (song_key) DO UPDATE SET
                query        = excluded.query,
                results_json = excluded.results_json,
                searched_at  = excluded.searched_at;
            """,
            new { songKey, query, json = JsonSerializer.Serialize(results) });
    }

    public async Task SetDecisionAsync(string songKey, SpotifyMatchState? decision, string? videoId = null)
    {
        var value = decision switch
        {
            null => null,
            SpotifyMatchState.Accepted => "accepted",
            SpotifyMatchState.Rejected => "rejected",
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Only accepted or rejected are stored."),
        };
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE spotify_matches SET decision = @value, video_id = @videoId WHERE song_key = @songKey;",
            new { songKey, value, videoId = decision == SpotifyMatchState.Accepted ? videoId : null });
    }

    public async Task SetTrackAsync(string songKey, long trackId, string videoId)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE spotify_matches SET decision = 'accepted', video_id = @videoId, track_id = @trackId WHERE song_key = @songKey;",
            new { songKey, trackId, videoId });
    }

    private static SpotifySearchRecord ToRecord(Row row) => new(
        row.SongKey,
        row.Query,
        JsonSerializer.Deserialize<List<YtSearchResult>>(row.ResultsJson) ?? [],
        DateTimeOffset.Parse(row.SearchedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
        row.Decision switch
        {
            "accepted" => SpotifyMatchState.Accepted,
            "rejected" => SpotifyMatchState.Rejected,
            _ => null,
        },
        row.VideoId,
        row.TrackId);

    private sealed class Row
    {
        public string SongKey { get; init; } = string.Empty;
        public string Query { get; init; } = string.Empty;
        public string ResultsJson { get; init; } = "[]";
        public string SearchedAt { get; init; } = string.Empty;
        public string? Decision { get; init; }
        public string? VideoId { get; init; }
        public long? TrackId { get; init; }
    }
}
