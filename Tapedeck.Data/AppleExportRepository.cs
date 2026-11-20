using Dapper;
using Tapedeck.Core.Apple;

namespace Tapedeck.Data;

public sealed class AppleExportRepository(SqliteConnectionFactory connectionFactory) : IAppleExportRepository
{
    // a downloaded track counts as current when its copy was made from this very file, download and values
    private const string CURRENT = """
        e.source_path = t.file_path
        AND e.source_downloaded_at IS t.downloaded_at
        AND e.source_edited_at IS t.edited_at
        """;

    private const string DOWNLOADED = "t.status = 'done' AND t.file_path IS NOT NULL";

    private const string SELECT_RECORD = """
        SELECT e.track_id, e.file_path, e.source_path, e.source_downloaded_at, e.source_edited_at
          FROM apple_exports e
        """;

    public async Task<IReadOnlyList<AppleExportCandidate>> ListPendingAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<CandidateRow>(
            $"""
            SELECT t.id AS track_id, t.file_path AS source_path, t.downloaded_at, t.edited_at,
                   e.file_path AS export_path, e.source_path AS export_source_path,
                   e.source_downloaded_at AS export_downloaded_at, e.source_edited_at AS export_edited_at
              FROM tracks t
              LEFT JOIN apple_exports e ON e.track_id = t.id
             WHERE {DOWNLOADED}
               AND (e.track_id IS NULL OR NOT ({CURRENT}))
             ORDER BY t.added_at, t.id;
            """);
        return rows.Select(r => new AppleExportCandidate(
            r.TrackId, r.SourcePath, r.DownloadedAt, r.EditedAt,
            r.ExportPath is null
                ? null
                : new AppleExportRecord(r.TrackId, r.ExportPath, r.ExportSourcePath!, r.ExportDownloadedAt, r.ExportEditedAt))).ToList();
    }

    public async Task<IReadOnlyList<AppleExportRecord>> ListOrphansAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<RecordRow>(
            $"""
            {SELECT_RECORD}
              LEFT JOIN tracks t ON t.id = e.track_id
             WHERE t.id IS NULL OR NOT ({DOWNLOADED});
            """);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<IReadOnlyDictionary<long, AppleExportRecord>> ListAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<RecordRow>($"{SELECT_RECORD};");
        return rows.Select(ToRecord).ToDictionary(r => r.TrackId);
    }

    public async Task<AppleExportRecord?> GetAsync(long trackId)
    {
        await using var connection = await connectionFactory.OpenAsync();
        var row = await connection.QuerySingleOrDefaultAsync<RecordRow>(
            $"{SELECT_RECORD} WHERE e.track_id = @trackId;", new { trackId });
        return row is null ? null : ToRecord(row);
    }

    public async Task<bool> IsCurrentAsync(long trackId)
    {
        await using var connection = await connectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<bool>(
            $"""
            SELECT EXISTS (
                SELECT 1 FROM tracks t JOIN apple_exports e ON e.track_id = t.id
                 WHERE t.id = @trackId AND {DOWNLOADED} AND {CURRENT});
            """, new { trackId });
    }

    public async Task SaveAsync(AppleExportRecord record)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO apple_exports (track_id, file_path, source_path, source_downloaded_at, source_edited_at)
            VALUES (@TrackId, @FilePath, @SourcePath, @SourceDownloadedAt, @SourceEditedAt)
            ON CONFLICT (track_id) DO UPDATE SET
                file_path            = excluded.file_path,
                source_path          = excluded.source_path,
                source_downloaded_at = excluded.source_downloaded_at,
                source_edited_at     = excluded.source_edited_at,
                exported_at          = excluded.exported_at;
            """,
            record);
    }

    public async Task DeleteAsync(long trackId)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync("DELETE FROM apple_exports WHERE track_id = @trackId;", new { trackId });
    }

    public async Task ClearAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync("DELETE FROM apple_exports;");
    }

    public async Task<AppleExportCounts> CountAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var row = await connection.QuerySingleAsync<CountRow>(
            $"""
            SELECT COUNT(*) AS total,
                   COALESCE(SUM(e.track_id IS NOT NULL AND {CURRENT}), 0) AS current
              FROM tracks t
              LEFT JOIN apple_exports e ON e.track_id = t.id
             WHERE {DOWNLOADED};
            """);
        return new AppleExportCounts((int)row.Current, (int)row.Total);
    }

    private static AppleExportRecord ToRecord(RecordRow r) =>
        new(r.TrackId, r.FilePath, r.SourcePath, r.SourceDownloadedAt, r.SourceEditedAt);

    private sealed class RecordRow
    {
        public long TrackId { get; init; }
        public string FilePath { get; init; } = string.Empty;
        public string SourcePath { get; init; } = string.Empty;
        public string? SourceDownloadedAt { get; init; }
        public string? SourceEditedAt { get; init; }
    }

    private sealed class CandidateRow
    {
        public long TrackId { get; init; }
        public string SourcePath { get; init; } = string.Empty;
        public string? DownloadedAt { get; init; }
        public string? EditedAt { get; init; }
        public string? ExportPath { get; init; }
        public string? ExportSourcePath { get; init; }
        public string? ExportDownloadedAt { get; init; }
        public string? ExportEditedAt { get; init; }
    }

    private sealed class CountRow
    {
        public long Total { get; init; }
        public long Current { get; init; }
    }
}
