-- YouTube searches for songs of a Spotify streaming history, keyed by SpotifyAnalyzer.SongKey.
-- All results are kept, so changed matching rules apply without searching again; sure, unsure and not found
-- are judged when read. Only the user's decision is stored. No plays or other history data are stored here.

CREATE TABLE spotify_matches (
    song_key     TEXT PRIMARY KEY,
    query        TEXT NOT NULL,
    results_json TEXT NOT NULL,
    searched_at  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now')),
    -- NULL: the matcher decides; 'accepted' with video_id: the user chose that video; 'rejected': none of them
    decision     TEXT CHECK (decision IN ('accepted', 'rejected')),
    video_id     TEXT,
    -- the track the song was imported as or found in the library
    track_id     INTEGER REFERENCES tracks (id) ON DELETE SET NULL
);
