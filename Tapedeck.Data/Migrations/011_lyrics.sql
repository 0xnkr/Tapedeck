-- Lyrics from LRCLIB and the user's own, kept apart like the other fields: the user's lyrics win, an empty
-- correction lets LRCLIB's show again. Not part of tracks_display, so the long texts stay out of every list query.

-- What LRCLIB has for the track: plain text, and the timed LRC only when its length fits the file.
ALTER TABLE tracks ADD COLUMN lrclib_plain TEXT;
ALTER TABLE tracks ADD COLUMN lrclib_synced TEXT;
ALTER TABLE tracks ADD COLUMN lrclib_instrumental INTEGER NOT NULL DEFAULT 0;

-- When LRCLIB was last asked, also when it had nothing; such tracks are asked again only after better names
-- from MusicBrainz.
ALTER TABLE tracks ADD COLUMN lyrics_checked_at TEXT;

-- The user's own lyrics, with or without times.
ALTER TABLE tracks ADD COLUMN user_lyrics TEXT;
