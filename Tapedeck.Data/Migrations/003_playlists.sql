-- Playlists become usable: unique names (one .m3u8 file each) and a track at most once per playlist.
-- Both tables were unused until now, so they are recreated instead of migrated.

DROP INDEX IF EXISTS ix_playlist_tracks_track;
DROP TABLE playlist_tracks;
DROP TABLE playlists;

CREATE TABLE playlists (
    id         INTEGER PRIMARY KEY,
    -- also the name of the exported .m3u8 file, hence unique regardless of case
    name       TEXT NOT NULL UNIQUE COLLATE NOCASE,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now')),
    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now'))
);

CREATE TABLE playlist_tracks (
    playlist_id INTEGER NOT NULL REFERENCES playlists (id) ON DELETE CASCADE,
    track_id    INTEGER NOT NULL REFERENCES tracks (id) ON DELETE CASCADE,
    -- 0-based order inside the playlist, kept without gaps by the repository
    position    INTEGER NOT NULL,
    PRIMARY KEY (playlist_id, track_id)
);

CREATE INDEX ix_playlist_tracks_order ON playlist_tracks (playlist_id, position);
CREATE INDEX ix_playlist_tracks_track ON playlist_tracks (track_id);
