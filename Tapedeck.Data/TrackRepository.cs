using Dapper;
using Tapedeck.Core;
using Tapedeck.Core.Models;

namespace Tapedeck.Data;

public sealed class TrackRepository(SqliteConnectionFactory connectionFactory) : ITrackRepository
{
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

    public async Task SetThumbPathAsync(long id, string thumbPath)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync("UPDATE tracks SET thumb_path = @thumbPath WHERE id = @id;", new { id, thumbPath });
    }

    public async Task SetCoverAsync(long id, string coverPath, string coverSource)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE tracks SET cover_path = @coverPath, cover_source = @coverSource WHERE id = @id;",
            new { id, coverPath, coverSource });
    }

    public async Task SetUserFieldsAsync(long id, UserFields f)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            """
            UPDATE tracks SET
                user_artist       = @Artist,
                user_title        = @Title,
                user_album        = @Album,
                user_album_artist = @AlbumArtist,
                user_year         = @Year,
                notes             = @Notes
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
        await connection.ExecuteAsync(
            "INSERT INTO tags (name) VALUES (@name) ON CONFLICT (name) DO NOTHING;", new { name }, transaction);
        await connection.ExecuteAsync(
            """
            INSERT INTO track_tags (track_id, tag_id, source)
            SELECT @trackId, id, @source FROM tags WHERE name = @name
            ON CONFLICT DO NOTHING;
            """,
            new { trackId, name, source = source.ToDb() }, transaction);
        await transaction.CommitAsync();
    }
}
