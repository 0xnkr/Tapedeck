-- All of an artist's Last.fm tags with some weight, not only the ones that map to a main genre. They go into the
-- files as TAPEDECK_TAGS, where a phone player compares songs by them ("play similar songs").
-- Keyed by MbNormalizer.Normalize of the shown artist, so "Beyoncé" and "Beyonce" share their tags.

CREATE TABLE artist_tags (
    artist_key TEXT    NOT NULL,
    tag        TEXT    NOT NULL,
    -- Last.fm's weight, 100 for the artist's strongest tag
    count      INTEGER NOT NULL,
    fetched_at TEXT    NOT NULL,
    PRIMARY KEY (artist_key, tag)
);

-- When Last.fm was asked for an artist's tags, also when it had none, so it is not asked again on every start.
CREATE TABLE artist_tags_checked (
    artist_key TEXT PRIMARY KEY,
    fetched_at TEXT NOT NULL
);
