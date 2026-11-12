using System.Data.Common;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Tapedeck.Core;
using Tapedeck.Core.Lyrics;
using Tapedeck.Core.Models;
using Tapedeck.Core.MusicBrainz;
using Tapedeck.Core.Recommendations;

namespace Tapedeck.Data;

public sealed class TrackRepository(SqliteConnectionFactory connectionFactory) : ITrackRepository
{
    // Milliseconds, so an edit right after writing the tags still counts as newer
    private const string NOW = "strftime('%Y-%m-%dT%H:%M:%fZ', 'now')";

    private const string HISTORY_DATE_FORMAT = "yyyy-MM-dd";

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

    public async Task<IReadOnlyList<(long Id, string Artist)>> ListWithoutGenresAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<(long Id, string Artist)>(
            """
            SELECT d.id, d.artist FROM tracks_display d JOIN tracks t ON t.id = d.id
            WHERE d.genres_source IN ('none', 'yt') AND d.artist <> '' AND t.lastfm_checked_at IS NULL
            ORDER BY d.added_at, d.id;
            """);
        return rows.AsList();
    }

    public async Task SetLastFmTagsAsync(long trackId, IReadOnlyList<string> names)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "DELETE FROM track_tags WHERE track_id = @trackId AND source = @source;",
            new { trackId, source = TagSource.Lastfm.ToDb() }, transaction);
        foreach (var name in names)
            await InsertTagAsync(connection, transaction, trackId, name, TagSource.Lastfm);
        var edited = names.Count > 0 ? $", edited_at = {NOW}" : string.Empty;
        await connection.ExecuteAsync(
            $"UPDATE tracks SET lastfm_checked_at = {NOW}{edited} WHERE id = @trackId;", new { trackId }, transaction);
        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<ArtistTag>> GetArtistTagsAsync(string artistKey)
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<(string Tag, long Count)>(
            "SELECT tag, count FROM artist_tags WHERE artist_key = @artistKey ORDER BY count DESC, tag;", new { artistKey });
        return rows.Select(r => new ArtistTag(r.Tag, (int)r.Count)).ToList();
    }

    public async Task<IReadOnlySet<string>> ListCheckedArtistKeysAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var keys = await connection.QueryAsync<string>("SELECT artist_key FROM artist_tags_checked;");
        return keys.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<bool> SaveArtistTagsAsync(string artistKey, IReadOnlyList<ArtistTag> tags, IReadOnlyList<long> trackIds)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var before = (await connection.QueryAsync<(string Tag, long Count)>(
                "SELECT tag, count FROM artist_tags WHERE artist_key = @artistKey;", new { artistKey }, transaction))
            .Select(r => (r.Tag, (int)r.Count))
            .OrderBy(r => r.Tag, StringComparer.Ordinal)
            .ToList();
        var after = tags.Select(t => (t.Tag, t.Count)).OrderBy(r => r.Tag, StringComparer.Ordinal).ToList();
        var changed = !before.SequenceEqual(after);

        await connection.ExecuteAsync(
            $"""
            INSERT INTO artist_tags_checked (artist_key, fetched_at) VALUES (@artistKey, {NOW})
            ON CONFLICT (artist_key) DO UPDATE SET fetched_at = excluded.fetched_at;
            """,
            new { artistKey }, transaction);
        if (changed)
        {
            await connection.ExecuteAsync("DELETE FROM artist_tags WHERE artist_key = @artistKey;", new { artistKey }, transaction);
            foreach (var tag in tags)
                await connection.ExecuteAsync(
                    $"INSERT INTO artist_tags (artist_key, tag, count, fetched_at) VALUES (@artistKey, @tag, @count, {NOW});",
                    new { artistKey, tag = tag.Tag, count = tag.Count }, transaction);
            foreach (var trackId in trackIds)
                await connection.ExecuteAsync(
                    $"UPDATE tracks SET edited_at = {NOW} WHERE id = @trackId;", new { trackId }, transaction);
        }
        await transaction.CommitAsync();
        return changed;
    }

    public async Task<ListeningHistory?> GetListeningHistoryAsync(long id)
    {
        await using var connection = await connectionFactory.OpenAsync();
        var row = await connection.QuerySingleOrDefaultAsync<(long? Plays, long? Skips, string? Last)>(
            "SELECT spotify_plays, spotify_early_skips, spotify_last_played FROM tracks WHERE id = @id;", new { id });
        if (row.Plays is null && row.Skips is null)
            return null;
        DateOnly? last = DateOnly.TryParseExact(row.Last, HISTORY_DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;
        return new ListeningHistory((int)(row.Plays ?? 0), (int)(row.Skips ?? 0), last);
    }

    public async Task<IReadOnlyList<long>> SetListeningHistoryAsync(IReadOnlyDictionary<long, ListeningHistory> byTrack)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var ids = await connection.QueryAsync<long>("SELECT id FROM tracks;", transaction: transaction);
        var changed = new List<long>();
        foreach (var id in ids)
        {
            var history = byTrack.GetValueOrDefault(id);
            long? plays = history?.Plays;
            long? skips = history?.EarlySkips;
            var last = history?.LastPlayed?.ToString(HISTORY_DATE_FORMAT, CultureInfo.InvariantCulture);
            var rows = await connection.ExecuteAsync(
                $"""
                UPDATE tracks SET
                    spotify_plays       = @plays,
                    spotify_early_skips = @skips,
                    spotify_last_played = @last,
                    edited_at           = {NOW}
                WHERE id = @id
                  AND NOT (spotify_plays IS @plays AND spotify_early_skips IS @skips AND spotify_last_played IS @last);
                """,
                new { id, plays, skips, last }, transaction);
            if (rows > 0)
                changed.Add(id);
        }
        await transaction.CommitAsync();
        return changed;
    }

    public async Task MarkTagsWrittenAsync(long id)
    {
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync($"UPDATE tracks SET tags_written_at = {NOW} WHERE id = @id;", new { id });
    }

    public async Task<IReadOnlyList<long>> MarkGainTagsOutdatedAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        // untagged files are outdated anyway; without a measurement there is no gain field to change
        var ids = await connection.QueryAsync<long>(
            $"""
            UPDATE tracks SET edited_at = {NOW}
            WHERE file_path IS NOT NULL
              AND tags_written_at IS NOT NULL
              AND loudness_lufs IS NOT NULL
            RETURNING id;
            """);
        return ids.ToList();
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

    public async Task<TrackLyrics> GetLyricsAsync(long id)
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<(string? User, string? Synced, string? Plain, long Instrumental, string? CheckedAt)>(
            """
            SELECT user_lyrics, lrclib_synced, lrclib_plain, lrclib_instrumental, lyrics_checked_at
            FROM tracks WHERE id = @id;
            """,
            new { id });
        return rows.Select(r => new TrackLyrics(r.User, r.Synced, r.Plain, r.Instrumental != 0, r.CheckedAt))
            .FirstOrDefault() ?? TrackLyrics.NONE;
    }

    public async Task SaveLrclibLyricsAsync(long id, LrclibLyrics lyrics)
    {
        await using var connection = await connectionFactory.OpenAsync();
        // the shown text changes only without lyrics of the user's own
        await connection.ExecuteAsync(
            $"""
            UPDATE tracks SET
                edited_at = CASE
                    WHEN user_lyrics IS NULL AND COALESCE(lrclib_synced, lrclib_plain) IS NOT COALESCE(@synced, @plain) THEN {NOW}
                    ELSE edited_at END,
                lrclib_plain        = @plain,
                lrclib_synced       = @synced,
                lrclib_instrumental = @instrumental,
                lyrics_checked_at   = {NOW}
            WHERE id = @id;
            """,
            new { id, plain = lyrics.Plain, synced = lyrics.Synced, instrumental = lyrics.Instrumental });
    }

    public async Task SetUserLyricsAsync(long id, string? text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? null : text;
        await using var connection = await connectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            $"""
            UPDATE tracks SET
                edited_at   = CASE WHEN user_lyrics IS @value THEN edited_at ELSE {NOW} END,
                user_lyrics = @value
            WHERE id = @id;
            """,
            new { id, value });
    }

    public async Task<IReadOnlyList<LyricsRequest>> ListWithoutLyricsAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<(long Id, string Artist, string Title, string? Album, long? DurationS, long IsTopic)>(
            $"""
            SELECT {LYRICS_REQUEST_COLUMNS} FROM tracks_display d JOIN tracks t ON t.id = d.id
            WHERE t.status = 'done' AND t.file_path IS NOT NULL AND d.artist <> '' AND d.title <> ''
              AND (t.lyrics_checked_at IS NULL
                   OR (t.lrclib_plain IS NULL AND t.lrclib_synced IS NULL AND t.lrclib_instrumental = 0
                       AND t.mb_checked_at > t.lyrics_checked_at))
            ORDER BY d.added_at, d.id;
            """);
        return rows.Select(ToRequest).ToList();
    }

    public async Task<LyricsRequest?> GetLyricsRequestAsync(long id)
    {
        await using var connection = await connectionFactory.OpenAsync();
        var rows = await connection.QueryAsync<(long Id, string Artist, string Title, string? Album, long? DurationS, long IsTopic)>(
            $"SELECT {LYRICS_REQUEST_COLUMNS} FROM tracks_display d JOIN tracks t ON t.id = d.id WHERE d.id = @id;", new { id });
        return rows.Select(ToRequest).FirstOrDefault();
    }

    // a YouTube Music upload, recognized as YtSearchResult.IsTopic does it
    private const string LYRICS_REQUEST_COLUMNS =
        """
        d.id, d.artist, d.title, d.album, d.duration_s,
        (COALESCE(json_extract(t.info_json, '$.description'), '') LIKE 'Provided to YouTube by%'
         OR COALESCE(t.channel, '') LIKE '% - Topic') AS is_topic
        """;

    private static LyricsRequest ToRequest((long Id, string Artist, string Title, string? Album, long? DurationS, long IsTopic) row) =>
        new(row.Id, row.Artist, row.Title, string.IsNullOrEmpty(row.Album) ? null : row.Album, (int?)row.DurationS, row.IsTopic != 0);


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
