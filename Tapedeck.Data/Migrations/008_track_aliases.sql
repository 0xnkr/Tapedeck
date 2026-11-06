-- Videos of duplicates that were merged into another track ("Keep this one"). Adding such a video again, by link,
-- CSV or a playlist sync, maps it to the kept track instead of bringing the duplicate back.
-- A video is either a track (tracks.video_id) or an alias, never both.

CREATE TABLE track_aliases (
    video_id TEXT PRIMARY KEY,
    track_id INTEGER NOT NULL REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_track_aliases_track ON track_aliases (track_id);
