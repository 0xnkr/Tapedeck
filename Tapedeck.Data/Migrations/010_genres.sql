-- From now on only the main genres (Core/Genres/GenreMap) go into the files instead of every fine genre.
-- Files tagged before carry the fine ones: marking them outdated puts them into "Write tags (N)",
-- so the user decides when they are rewritten.

UPDATE tracks
SET edited_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
WHERE file_path IS NOT NULL
  AND tags_written_at IS NOT NULL
  AND EXISTS (SELECT 1 FROM track_tags tt WHERE tt.track_id = tracks.id);

-- When Last.fm was last asked for the artist's genres of a track without genres of its own, also when it had none;
-- such tracks are not asked again on every start.
ALTER TABLE tracks ADD COLUMN lastfm_checked_at TEXT;
