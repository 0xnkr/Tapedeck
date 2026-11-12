-- What the user's Spotify streaming history says about a library track, as a starting taste for the phone app's
-- recommendations. Raw counts and a date only; the phone applies its own decay. NULL when the history has nothing
-- on the track or none was taken over. Goes into the files as TAPEDECK_HISTORY.

-- Real plays after SpotifyPlayRules.DEFAULT.
ALTER TABLE tracks ADD COLUMN spotify_plays INTEGER;

-- Plays ended with the forward button within the first 10 seconds.
ALTER TABLE tracks ADD COLUMN spotify_early_skips INTEGER;

-- The day (UTC, yyyy-MM-dd) of the latest play or early skip.
ALTER TABLE tracks ADD COLUMN spotify_last_played TEXT;
