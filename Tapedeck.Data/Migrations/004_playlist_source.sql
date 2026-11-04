-- A playlist can be linked to a YouTube playlist, whose new videos a sync adds.

ALTER TABLE playlists ADD COLUMN source_url TEXT;

-- one Tapedeck playlist per YouTube playlist; importing it again syncs the existing one
CREATE UNIQUE INDEX ux_playlists_source ON playlists (source_url) WHERE source_url IS NOT NULL;
