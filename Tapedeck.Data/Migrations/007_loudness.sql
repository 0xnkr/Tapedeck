-- Integrated loudness of the downloaded file in LUFS (EBU R 128), measured with ffmpeg's ebur128 filter.
-- TagWriter turns it into R128_TRACK_GAIN, so players on the phone play all tracks equally loud.
-- NULL: not measured yet (older downloads, adopted files); a re-download measures again.

ALTER TABLE tracks ADD COLUMN loudness_lufs REAL;
