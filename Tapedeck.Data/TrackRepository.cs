using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Models;
using Tapedeck.Core.MusicBrainz;

namespace Tapedeck.Data;

public sealed class TrackRepository(SqliteConnectionFactory connectionFactory) : ITrackRepository
{
    // Milliseconds, so an edit right after writing the tags still counts as newer
    private const string NOW = "strftime('%Y-%m-%dT%H:%M:%fZ', 'now')";

    public async Task<long> AddAsync(string videoId, string url)
    {
        await using var connection = await connectionFactory.OpenAsync();
        var id = await connection.ExecuteScalarAsync<long?>(
            """
            INSERT INTO tracks (video_id, url) VALUES (@videoId, @url)
            ON CONFLICT (video_id) DO NOTHING
            RETURNING id;
            """,
            new { videoId, url });
        return id ?? await connection.ExecuteScalarAsync<long>(
            "SELECT id FROM tracks WHERE video_id = @videoId;", new { videoId });
    }

    public async Task<Track?> GetAsync(long id)
    {
        await using var connection = await connectionFactory.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Track>("SELECT * FROM tracks WHERE id = @id;", new { id });
    }

    public async Task<TrackDisplay?> GetDisplayAsync(long id)
    {
        await using var connection = await connectionFactory.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<TrackDisplay>(
            "SELECT * FROM tracks_display WHERE id = @id;", new { id });
    }

    public async Task<IReadOnlyList<TrackDisplay>> ListDisplayAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<TrackDisplay>("SELECT * FROM tracks_display ORDER BY added_at DESC, id DESC;");
        return rows.AsList();
    }

    public async Task UpdateYtMetadataAsync(long id, YtMetadata m)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            """
            UPDATE tracks SET
                yt_title        = @Title,
                channel         = @Channel,
                upload_date     = @UploadDate,
                duration_s      = @DurationS,
                yt_track        = @Track,
                yt_artist       = @Artist,
                yt_album        = @Album,
                yt_release_year = @ReleaseYear,
                parsed_artist   = @ParsedArtist,
                parsed_title    = @ParsedTitle,
                info_json       = @InfoJson
            WHERE id = @id;
            """,
            new
            {
                id, m.Title, m.Channel, m.UploadDate, m.DurationS, m.Track, m.Artist, m.Album,
                m.ReleaseYear, m.ParsedArtist, m.ParsedTitle, m.InfoJson,
            });
    }

    public async Task UpdateDownloadStateAsync(long id, DownloadStatus status, string? filePath = null, string? fileFormat = null, string? errorMsg = null)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            """
            UPDATE tracks SET
                status        = @status,
                file_path     = COALESCE(@filePath, file_path),
                file_format   = COALESCE(@fileFormat, file_format),
                downloaded_at = CASE WHEN @status = 'done' THEN strftime('%Y-%m-%dT%H:%M:%SZ', 'now') ELSE downloaded_at END,
                error_msg     = @errorMsg
            WHERE id = @id;
            """,
            new { id, status = status.ToDb(), filePath, fileFormat, errorMsg });
    }

    public async Task<int> ResetInterruptedDownloadsAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        return await connection.ExecuteAsync(
            "UPDATE tracks SET status = @pending WHERE status = @downloading;",
            new { pending = DownloadStatus.Pending.ToDb(), downloading = DownloadStatus.Downloading.ToDb() });
    }

    public async Task SetFilePathAsync(long id, string filePath)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync("UPDATE tracks SET file_path = @filePath WHERE id = @id;", new { id, filePath });
    }

    public async Task DeleteAsync(long id)
    {
        await using var connection = await connectionFactory.OpenAsync();
        // track_tags and playlist_tracks follow via ON DELETE CASCADE
        await connection.ExecuteAsync("DELETE FROM tracks WHERE id = @id;", new { id });
    }

    public async Task SetThumbPathAsync(long id, string thumbPath)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync("UPDATE tracks SET thumb_path = @thumbPath WHERE id = @id;", new { id, thumbPath });
    }

    public async Task SetCoverAsync(long id, string coverPath, string coverSource)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            $"""
            UPDATE tracks SET
                edited_at    = CASE WHEN cover_path IS @coverPath THEN edited_at ELSE {NOW} END,
                cover_path   = @coverPath,
                cover_source = @coverSource
            WHERE id = @id;
            """,
            new { id, coverPath, coverSource });
    }

    public async Task<IReadOnlyList<long>> ListWithoutCoverAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<long>(
            "SELECT id FROM tracks WHERE thumb_path IS NOT NULL AND cover_path IS NULL ORDER BY added_at, id;");
        return rows.AsList();
    }

    public async Task SetUserFieldsAsync(long id, UserFields f)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            $"""
            UPDATE tracks SET
                user_artist       = @Artist,
                user_title        = @Title,
                user_album        = @Album,
                user_album_artist = @AlbumArtist,
                user_year         = @Year,
                notes             = @Notes,
                edited_at         = {NOW}
            WHERE id = @id;
            """,
            new { id, f.Artist, f.Title, f.Album, f.AlbumArtist, f.Year, f.Notes });
    }

    public async Task SetRatingAsync(long id, int? rating)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync("UPDATE tracks SET rating = @rating WHERE id = @id;", new { id, rating });
    }

    public async Task AddTagAsync(long trackId, string name, TagSource source)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await InsertTagAsync(connection, transaction, trackId, name, source);
        await transaction.CommitAsync();
    }

    public async Task SetUserTagsAsync(long trackId, IReadOnlyList<string> names)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "DELETE FROM track_tags WHERE track_id = @trackId AND source = @source;",
            new { trackId, source = TagSource.User.ToDb() }, transaction);
        foreach (var name in names)
            await InsertTagAsync(connection, transaction, trackId, name, TagSource.User);
        await connection.ExecuteAsync(
            $"UPDATE tracks SET edited_at = {NOW} WHERE id = @trackId;", new { trackId }, transaction);
        await transaction.CommitAsync();
    }

    public async Task MarkTagsWrittenAsync(long id)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync($"UPDATE tracks SET tags_written_at = {NOW} WHERE id = @id;", new { id });
    }

    public async Task<double?> GetLoudnessAsync(long id)
    {
        await using var connection = await connectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<double?>("SELECT loudness_lufs FROM tracks WHERE id = @id;", new { id });
    }

    public async Task SetLoudnessAsync(long id, double lufs)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            $"""
            UPDATE tracks SET
                edited_at     = CASE WHEN loudness_lufs IS @lufs THEN edited_at ELSE {NOW} END,
                loudness_lufs = @lufs
            WHERE id = @id;
            """,
            new { id, lufs });
    }

    public async Task<IReadOnlyList<long>> ListWithoutLoudnessAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<long>(
            """
            SELECT id FROM tracks
            WHERE status = 'done' AND file_path IS NOT NULL AND loudness_lufs IS NULL
            ORDER BY added_at, id;
            """);
        return rows.AsList();
    }

    public async Task<long?> GetAliasTargetAsync(string videoId)
    {
        await using var connection = await connectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<long?>(
            "SELECT track_id FROM track_aliases WHERE video_id = @videoId;", new { videoId });
    }

    public async Task<IReadOnlyDictionary<string, long>> ListAliasesAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<(string VideoId, long TrackId)>("SELECT video_id, track_id FROM track_aliases;");
        return rows.ToDictionary(r => r.VideoId, r => r.TrackId, StringComparer.Ordinal);
    }

    public async Task MergeIntoAsync(long keepId, IReadOnlyList<long> mergedIds)
    {
        var merged = mergedIds.Where(id => id != keepId).Distinct().ToArray();
        if (merged.Length == 0)
            return;

        await using var connection = await connectionFactory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // their videos and the videos merged into them earlier now lead to the kept track
        await connection.ExecuteAsync(
            """
            INSERT INTO track_aliases (video_id, track_id)
            SELECT video_id, @keepId FROM tracks WHERE id IN @merged
            ON CONFLICT (video_id) DO UPDATE SET track_id = excluded.track_id;
            UPDATE track_aliases SET track_id = @keepId WHERE track_id IN @merged;
            """,
            new { keepId, merged }, transaction);

        // one by one: two merged tracks in the same playlist must not both become the kept one
        foreach (var mergedId in merged)
        {
            await connection.ExecuteAsync(
                """
                DELETE FROM playlist_tracks
                 WHERE track_id = @mergedId
                   AND playlist_id IN (SELECT playlist_id FROM playlist_tracks WHERE track_id = @keepId);
                UPDATE playlist_tracks SET track_id = @keepId WHERE track_id = @mergedId;
                """,
                new { keepId, mergedId }, transaction);
        }
        // dropped places leave gaps; positions stay 0..n-1
        await connection.ExecuteAsync(
            """
            UPDATE playlist_tracks
               SET position = (SELECT COUNT(*) FROM playlist_tracks o
                                WHERE o.playlist_id = playlist_tracks.playlist_id AND o.position < playlist_tracks.position);
            """,
            transaction: transaction);

        await connection.ExecuteAsync(
            """
            UPDATE tracks SET rating = (SELECT MAX(rating) FROM tracks WHERE id IN @merged)
             WHERE id = @keepId AND rating IS NULL;
            """,
            new { keepId, merged }, transaction);

        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<long>> ListByMbStateAsync(params MbMatchState[] states)
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<long>(
            "SELECT id FROM tracks WHERE mb_status IN @states ORDER BY added_at, id;",
            new { states = states.Select(s => s.ToDb()).ToArray() });
        return rows.AsList();
    }

    public async Task SaveMbResultAsync(long id, MbMatchState state, MbPick? applied, IReadOnlyList<string> genres,
        double? score, string? candidatesJson)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // the shown values change when a match comes or goes, so the file's tags become outdated
        var wasMatched = await connection.ExecuteScalarAsync<string>(
            "SELECT mb_status FROM tracks WHERE id = @id;", new { id }, transaction) == MbMatchState.Matched.ToDb();
        var edited = applied is not null || wasMatched;

        await connection.ExecuteAsync(
            $"""
            UPDATE tracks SET
                mb_status           = @status,
                mb_recording_id     = @RecordingId,
                mb_artist           = @Artist,
                mb_artist_id        = @ArtistId,
                mb_title            = @Title,
                mb_album            = @Album,
                mb_album_artist     = @AlbumArtist,
                mb_release_id       = @ReleaseId,
                mb_release_group_id = @ReleaseGroupId,
                mb_release_date     = @Date,
                mb_track_no         = @TrackNumber,
                mb_disc_no          = @DiscNumber,
                mb_score            = @Score,
                mb_candidates       = COALESCE(@candidatesJson, mb_candidates),
                mb_checked_at       = {NOW},
                edited_at           = CASE WHEN @edited THEN {NOW} ELSE edited_at END
            WHERE id = @id;
            """,
            new
            {
                id,
                status = (applied is null ? state : MbMatchState.Matched).ToDb(),
                RecordingId = applied?.RecordingId,
                Artist = applied?.Artist,
                ArtistId = applied?.ArtistId,
                Title = applied?.Title,
                Album = applied?.Album,
                AlbumArtist = applied?.AlbumArtist,
                ReleaseId = applied?.ReleaseId,
                ReleaseGroupId = applied?.ReleaseGroupId,
                Date = applied?.Date,
                TrackNumber = applied?.TrackNumber,
                DiscNumber = applied?.DiscNumber,
                Score = applied?.Score ?? score,
                candidatesJson,
                edited,
            },
            transaction);

        await connection.ExecuteAsync(
            "DELETE FROM track_tags WHERE track_id = @id AND source = @source;",
            new { id, source = TagSource.Mb.ToDb() }, transaction);
        if (applied is not null)
        {
            foreach (var genre in genres)
                await InsertTagAsync(connection, transaction, id, genre, TagSource.Mb);
        }

        // a MusicBrainz cover belongs to the previous release; the caller sets the new one once downloaded
        await connection.ExecuteAsync(
            "UPDATE tracks SET cover_path = NULL, cover_source = NULL WHERE id = @id AND cover_source = 'caa';",
            new { id }, transaction);

        await transaction.CommitAsync();
    }

    private static async Task InsertTagAsync(SqliteConnection connection, DbTransaction transaction,
        long trackId, string name, TagSource source)
    {
        await connection.ExecuteAsync(
            "INSERT INTO tags (name) VALUES (@name) ON CONFLICT (name) DO NOTHING;", new { name }, transaction);
        await connection.ExecuteAsync(
            """
            INSERT INTO track_tags (track_id, tag_id, source)
            SELECT @trackId, id, @source FROM tags WHERE name = @name
            ON CONFLICT DO NOTHING;
            """,
            new { trackId, name, source = source.ToDb() }, transaction);
    }
}
