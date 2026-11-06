using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.Playlists;

namespace Tapedeck.Data;

public sealed class PlaylistRepository(SqliteConnectionFactory connectionFactory) : IPlaylistRepository
{
    private const string NOW = "strftime('%Y-%m-%dT%H:%M:%SZ', 'now')";

    private const string SELECT_PLAYLISTS = """
        SELECT p.id,
               p.name,
               p.source_url,
               p.rules,
               COUNT(pt.track_id) AS track_count
          FROM playlists p
          LEFT JOIN playlist_tracks pt ON pt.playlist_id = p.id
        """;

    public async Task<IReadOnlyList<Playlist>> ListAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<Playlist>(
            $"{SELECT_PLAYLISTS} GROUP BY p.id ORDER BY p.name COLLATE NOCASE;");
        return await WithSmartCountsAsync(connection, rows.AsList());
    }

    public async Task<Playlist?> GetAsync(long id)
    {
        await using var connection = await connectionFactory.OpenAsync();
        var playlist = await connection.QuerySingleOrDefaultAsync<Playlist>(
            $"{SELECT_PLAYLISTS} WHERE p.id = @id GROUP BY p.id;", new { id });
        return playlist is null ? null : (await WithSmartCountsAsync(connection, [playlist]))[0];
    }

    public async Task<bool> NameExistsAsync(string name, long? exceptId = null)
    {
        await using var connection = await connectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM playlists WHERE name = @name AND id IS NOT @exceptId);",
            new { name, exceptId });
    }

    public async Task<Playlist?> FindBySourceAsync(string sourceUrl)
    {
        await using var connection = await connectionFactory.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Playlist>(
            $"{SELECT_PLAYLISTS} WHERE p.source_url = @sourceUrl GROUP BY p.id;", new { sourceUrl });
    }

    public async Task<long> CreateAsync(string name, string? sourceUrl = null)
    {
        await using var connection = await connectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "INSERT INTO playlists (name, source_url) VALUES (@name, @sourceUrl) RETURNING id;", new { name, sourceUrl });
    }

    public async Task<long> CreateSmartAsync(string name, SmartRules rules)
    {
        await using var connection = await connectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<long>(
            "INSERT INTO playlists (name, rules) VALUES (@name, @rules) RETURNING id;", new { name, rules = rules.ToJson() });
    }

    public async Task UpdateSmartAsync(long id, string name, SmartRules rules)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            $"UPDATE playlists SET name = @name, rules = @rules, updated_at = {NOW} WHERE id = @id AND rules IS NOT NULL;",
            new { id, name, rules = rules.ToJson() });
    }

    public async Task RenameAsync(long id, string name)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            $"UPDATE playlists SET name = @name, updated_at = {NOW} WHERE id = @id;", new { id, name });
    }

    public async Task DeleteAsync(long id)
    {
        await using var connection = await connectionFactory.OpenAsync();
        // playlist_tracks follow via ON DELETE CASCADE
        await connection.ExecuteAsync("DELETE FROM playlists WHERE id = @id;", new { id });
    }

    public async Task<IReadOnlyList<TrackDisplay>> GetTracksAsync(long playlistId)
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rules = await connection.ExecuteScalarAsync<string?>(
            "SELECT rules FROM playlists WHERE id = @playlistId;", new { playlistId });
        if (rules is not null)
            return SmartRules.FromJson(rules).Apply(await LibraryAsync(connection), DateTime.UtcNow);

        var rows = await connection.QueryAsync<TrackDisplay>(
            """
            SELECT d.*
              FROM playlist_tracks pt
              JOIN tracks_display d ON d.id = pt.track_id
             WHERE pt.playlist_id = @playlistId
             ORDER BY pt.position;
            """,
            new { playlistId });
        return rows.AsList();
    }

    public async Task<int> AddTracksAsync(long playlistId, IReadOnlyList<long> trackIds)
    {
        await using var connection = await connectionFactory.OpenAsync();
        // a smart playlist picks its tracks itself
        if (await connection.ExecuteScalarAsync<bool>(
                "SELECT rules IS NOT NULL FROM playlists WHERE id = @playlistId;", new { playlistId }))
            return 0;

        await using var transaction = await connection.BeginTransactionAsync();
        var added = 0;
        foreach (var trackId in trackIds)
        {
            added += await connection.ExecuteAsync(
                """
                INSERT INTO playlist_tracks (playlist_id, track_id, position)
                SELECT @playlistId, @trackId, COALESCE(MAX(position) + 1, 0)
                  FROM playlist_tracks WHERE playlist_id = @playlistId
                ON CONFLICT DO NOTHING;
                """,
                new { playlistId, trackId }, transaction);
        }
        await Touch(connection, playlistId, transaction);
        await transaction.CommitAsync();
        return added;
    }

    public async Task RemoveTrackAsync(long playlistId, long trackId)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "DELETE FROM playlist_tracks WHERE playlist_id = @playlistId AND track_id = @trackId;",
            new { playlistId, trackId }, transaction);
        await Renumber(connection, playlistId, transaction);
        await Touch(connection, playlistId, transaction);
        await transaction.CommitAsync();
    }

    public async Task MoveTrackAsync(long playlistId, long trackId, int newIndex)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var order = (await connection.QueryAsync<long>(
            "SELECT track_id FROM playlist_tracks WHERE playlist_id = @playlistId ORDER BY position;",
            new { playlistId }, transaction)).AsList();
        if (!order.Remove(trackId))
            return;

        order.Insert(Math.Clamp(newIndex, 0, order.Count), trackId);
        for (var i = 0; i < order.Count; i++)
        {
            await connection.ExecuteAsync(
                "UPDATE playlist_tracks SET position = @i WHERE playlist_id = @playlistId AND track_id = @trackId;",
                new { i, playlistId, trackId = order[i] }, transaction);
        }
        await Touch(connection, playlistId, transaction);
        await transaction.CommitAsync();
    }

    /// <summary>Smart playlists count the tracks their rules pick; the library is read once for all of them.</summary>
    private static async Task<IReadOnlyList<Playlist>> WithSmartCountsAsync(SqliteConnection connection, IReadOnlyList<Playlist> playlists)
    {
        if (!playlists.Any(p => p.IsSmart))
            return playlists;

        var library = await LibraryAsync(connection);
        var now = DateTime.UtcNow;
        return playlists.Select(p => !p.IsSmart ? p : new Playlist
        {
            Id = p.Id,
            Name = p.Name,
            SourceUrl = p.SourceUrl,
            Rules = p.Rules,
            TrackCount = SmartRules.FromJson(p.Rules).Apply(library, now).Count,
        }).ToList();
    }

    private static async Task<IReadOnlyList<TrackDisplay>> LibraryAsync(SqliteConnection connection) =>
        (await connection.QueryAsync<TrackDisplay>("SELECT * FROM tracks_display;")).AsList();

    /// <summary>Closes gaps after a removal, so positions stay 0..n-1.</summary>
    private static Task Renumber(SqliteConnection connection, long playlistId,
        DbTransaction transaction) =>
        connection.ExecuteAsync(
            """
            UPDATE playlist_tracks
               SET position = (SELECT COUNT(*) FROM playlist_tracks o
                                WHERE o.playlist_id = playlist_tracks.playlist_id AND o.position < playlist_tracks.position)
             WHERE playlist_id = @playlistId;
            """,
            new { playlistId }, transaction);

    private static Task Touch(SqliteConnection connection, long playlistId,
        DbTransaction transaction) =>
        connection.ExecuteAsync($"UPDATE playlists SET updated_at = {NOW} WHERE id = @playlistId;", new { playlistId }, transaction);
}
