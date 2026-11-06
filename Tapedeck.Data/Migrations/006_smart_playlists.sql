-- Smart playlists: rules as JSON (SmartRules) instead of a fixed list of tracks.
-- Their tracks are picked from the library whenever they are read, so playlist_tracks stays empty for them.

ALTER TABLE playlists ADD COLUMN rules TEXT;
