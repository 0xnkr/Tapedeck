-- MusicBrainz match: state, the chosen release and its place on it, and the candidates for the user.
-- The mb_* values are only filled for an applied match (mb_status = 'matched'); suggestions live in mb_candidates.

ALTER TABLE tracks ADD COLUMN mb_status TEXT NOT NULL DEFAULT 'none'
    CHECK (mb_status IN ('none', 'matched', 'suggested', 'no_match', 'rejected'));
ALTER TABLE tracks ADD COLUMN mb_artist_id TEXT;
ALTER TABLE tracks ADD COLUMN mb_release_id TEXT;
ALTER TABLE tracks ADD COLUMN mb_release_group_id TEXT;
ALTER TABLE tracks ADD COLUMN mb_album_artist TEXT;
ALTER TABLE tracks ADD COLUMN mb_track_no INTEGER;
ALTER TABLE tracks ADD COLUMN mb_disc_no INTEGER;
-- how well the song matched, 0..1
ALTER TABLE tracks ADD COLUMN mb_score REAL;
-- JSON array of MbPick: the best songs found, for confirming a suggestion
ALTER TABLE tracks ADD COLUMN mb_candidates TEXT;
ALTER TABLE tracks ADD COLUMN mb_checked_at TEXT;

CREATE INDEX ix_tracks_mb_status ON tracks (mb_status);

DROP VIEW tracks_display;

-- Best available value: user > mb > yt > parsed > fallback.
-- Each *_source column follows the same order as its COALESCE: user | mb | lastfm | yt | parsed | channel | derived | none.
-- Genres: only the tags of the best source present for the track (user > mb > lastfm > yt).
CREATE VIEW tracks_display AS
WITH genre_source AS (
    SELECT t.id AS track_id,
           (SELECT s.source
              FROM track_tags s
             WHERE s.track_id = t.id
             ORDER BY CASE s.source WHEN 'user' THEN 0 WHEN 'mb' THEN 1 WHEN 'lastfm' THEN 2 ELSE 3 END
             LIMIT 1) AS source
      FROM tracks t
)
SELECT
    t.id,
    t.video_id,
    t.url,
    t.added_at,
    t.channel,
    t.upload_date,

    COALESCE(t.user_artist, t.mb_artist, t.yt_artist, t.parsed_artist, t.channel, '')        AS artist,
    CASE WHEN t.user_artist   IS NOT NULL THEN 'user'
         WHEN t.mb_artist     IS NOT NULL THEN 'mb'
         WHEN t.yt_artist     IS NOT NULL THEN 'yt'
         WHEN t.parsed_artist IS NOT NULL THEN 'parsed'
         WHEN t.channel       IS NOT NULL THEN 'channel'
         ELSE 'none' END                                                                    AS artist_source,

    COALESCE(t.user_title, t.mb_title, t.yt_track, t.parsed_title, t.yt_title, t.video_id)  AS title,
    CASE WHEN t.user_title   IS NOT NULL THEN 'user'
         WHEN t.mb_title     IS NOT NULL THEN 'mb'
         WHEN t.yt_track     IS NOT NULL THEN 'yt'
         WHEN t.parsed_title IS NOT NULL THEN 'parsed'
         WHEN t.yt_title     IS NOT NULL THEN 'yt'
         ELSE 'none' END                                                                    AS title_source,

    COALESCE(t.user_album, t.mb_album, t.yt_album)                                          AS album,
    CASE WHEN t.user_album IS NOT NULL THEN 'user'
         WHEN t.mb_album   IS NOT NULL THEN 'mb'
         WHEN t.yt_album   IS NOT NULL THEN 'yt'
         ELSE 'none' END                                                                    AS album_source,

    -- a corrected artist outranks the release artist, it is what the user wants to see
    COALESCE(t.user_album_artist, t.user_artist, t.mb_album_artist, t.mb_artist, t.yt_artist, t.parsed_artist, t.channel, '') AS album_artist,
    CASE WHEN t.user_album_artist IS NOT NULL THEN 'user'
         WHEN t.user_artist       IS NULL AND t.mb_album_artist IS NOT NULL THEN 'mb'
         WHEN COALESCE(t.user_artist, t.mb_artist, t.yt_artist, t.parsed_artist, t.channel) IS NOT NULL THEN 'derived'
         ELSE 'none' END                                                                    AS album_artist_source,

    COALESCE(t.user_year, CAST(substr(t.mb_release_date, 1, 4) AS INTEGER), t.yt_release_year) AS year,
    CASE WHEN t.user_year       IS NOT NULL THEN 'user'
         WHEN t.mb_release_date IS NOT NULL THEN 'mb'
         WHEN t.yt_release_year IS NOT NULL THEN 'yt'
         ELSE 'none' END                                                                    AS year_source,

    (SELECT group_concat(g.name, ';' ORDER BY g.name)
       FROM track_tags tt
       JOIN tags g ON g.id = tt.tag_id
      WHERE tt.track_id = t.id
        AND tt.source = gs.source)                                                          AS genres,
    COALESCE(gs.source, 'none')                                                             AS genres_source,

    COALESCE(t.cover_path, t.thumb_path)                                                    AS cover,
    t.cover_source,
    t.mb_track_no                                                                           AS track_no,
    t.mb_disc_no                                                                            AS disc_no,
    t.mb_status,
    t.mb_score,
    t.mb_recording_id,
    t.mb_release_id,
    t.mb_release_group_id,
    t.mb_artist_id,
    t.duration_s,
    t.rating,
    t.notes,
    t.status,
    t.file_path,
    t.file_format,
    t.downloaded_at,
    t.error_msg,
    t.thumb_path,
    -- 1 when the file exists and values changed after the tags were last written
    COALESCE(t.file_path IS NOT NULL AND t.edited_at > COALESCE(t.tags_written_at, ''), 0)  AS tags_stale
FROM tracks t
JOIN genre_source gs ON gs.track_id = t.id;
