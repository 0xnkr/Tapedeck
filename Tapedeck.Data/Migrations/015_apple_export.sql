-- The .m4a copies of the Apple export (AppleExporter): one row per track that has a copy in the Apple folder.
-- Only files listed here belong to Tapedeck; anything else in that folder is never touched.
-- No foreign key on purpose: when a track goes, its row stays until the next export run has deleted the copy.
CREATE TABLE apple_exports (
    track_id             INTEGER PRIMARY KEY,
    file_path            TEXT    NOT NULL,  -- relative to the Apple folder
    -- the state of the library file the copy was made from; a difference means it is outdated
    source_path          TEXT    NOT NULL,  -- tracks.file_path, relative to the library folder
    source_downloaded_at TEXT,              -- tracks.downloaded_at: a new download means new audio
    source_edited_at     TEXT,              -- tracks.edited_at: new values mean new tags
    exported_at          TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now'))
);
