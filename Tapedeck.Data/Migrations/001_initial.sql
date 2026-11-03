-- Guiding idea: keep raw data (yt_*), enrichment (mb_*) and user corrections (user_*) separate.
-- The UI only reads the view tracks_display.

CREATE TABLE tracks (
    id              INTEGER PRIMARY KEY,
    video_id        TEXT    NOT NULL UNIQUE,
    url             TEXT    NOT NULL,
    added_at        TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now')),

    -- from yt-dlp -J
    yt_title        TEXT,
    channel         TEXT,
    upload_date     TEXT,
    duration_s      INTEGER,
    yt_track        TEXT,
    yt_artist       TEXT,
    yt_album        TEXT,
    yt_release_year INTEGER,
    -- fallback: yt_title split into "Artist - Title" by regex (TitleParser)
    parsed_artist   TEXT,
    parsed_title    TEXT,
    -- raw JSON without formats, requested_formats, automatic_captions, subtitles, heatmap, thumbnails
    info_json       TEXT,

    -- enrichment (MusicBrainz)
    mb_recording_id TEXT,
    mb_artist       TEXT,
    mb_title        TEXT,
    mb_album        TEXT,
    mb_release_date TEXT,

    -- user corrections
    user_artist       TEXT,
    user_title        TEXT,
    user_album        TEXT,
    user_album_artist TEXT,
    user_year         INTEGER,
    rating            INTEGER CHECK (rating BETWEEN 1 AND 5),
    notes             TEXT,

    -- Download
    status          TEXT    NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending', 'downloading', 'done', 'error')),
    file_path       TEXT,   -- relative to the library folder
    file_format     TEXT,
    downloaded_at   TEXT,
    error_msg       TEXT,

    -- images
    thumb_path      TEXT,   -- YouTube thumbnail (16:9), cached locally
    cover_path      TEXT,   -- square cover embedded in the file
    cover_source    TEXT    CHECK (cover_source IN ('thumb', 'caa', 'user'))
);

CREATE TABLE tags (
    id   INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE COLLATE NOCASE
);

CREATE TABLE track_tags (
    track_id INTEGER NOT NULL REFERENCES tracks (id) ON DELETE CASCADE,
    tag_id   INTEGER NOT NULL REFERENCES tags (id) ON DELETE CASCADE,
    source   TEXT    NOT NULL CHECK (source IN ('yt', 'lastfm', 'mb', 'user')),
    PRIMARY KEY (track_id, tag_id, source)
);

CREATE TABLE playlists (
    id         INTEGER PRIMARY KEY,
    name       TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now'))
);

CREATE TABLE playlist_tracks (
    playlist_id INTEGER NOT NULL REFERENCES playlists (id) ON DELETE CASCADE,
    track_id    INTEGER NOT NULL REFERENCES tracks (id) ON DELETE CASCADE,
    position    INTEGER NOT NULL,
    PRIMARY KEY (playlist_id, position)
);

CREATE INDEX ix_track_tags_tag ON track_tags (tag_id);
CREATE INDEX ix_playlist_tracks_track ON playlist_tracks (track_id);

-- Best available value: user > mb > yt > parsed > fallback.
-- Genres: only the tags of the best source present for the track (user > mb > lastfm > yt).
CREATE VIEW tracks_display AS
SELECT
    t.id,
    t.video_id,
    t.url,
    t.added_at,
    COALESCE(t.user_artist, t.mb_artist, t.yt_artist, t.parsed_artist, t.channel, '')        AS artist,
    COALESCE(t.user_title, t.mb_title, t.yt_track, t.parsed_title, t.yt_title, t.video_id)  AS title,
    COALESCE(t.user_album, t.mb_album, t.yt_album)                                          AS album,
    COALESCE(t.user_album_artist, t.user_artist, t.mb_artist, t.yt_artist, t.parsed_artist, t.channel, '') AS album_artist,
    COALESCE(t.user_year, CAST(substr(t.mb_release_date, 1, 4) AS INTEGER), t.yt_release_year) AS year,
    (SELECT group_concat(g.name, ';' ORDER BY g.name)
       FROM track_tags tt
       JOIN tags g ON g.id = tt.tag_id
      WHERE tt.track_id = t.id
        AND tt.source = (SELECT s.source
                           FROM track_tags s
                          WHERE s.track_id = t.id
                          ORDER BY CASE s.source WHEN 'user' THEN 0 WHEN 'mb' THEN 1 WHEN 'lastfm' THEN 2 ELSE 3 END
                          LIMIT 1))                                                         AS genres,
    COALESCE(t.cover_path, t.thumb_path)                                                    AS cover,
    t.duration_s,
    t.rating,
    t.notes,
    t.status,
    t.file_path,
    t.file_format,
    t.downloaded_at,
    t.error_msg,
    t.thumb_path
FROM tracks t;
