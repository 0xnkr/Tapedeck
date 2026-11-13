-- How the downloaded file sounds (tempo, timbre, energy, dynamics), measured by AudioAnalysis from the decoded audio.
-- Stored as the finished TAPEDECK_AUDIO text (AudioFeatures.Format), which TagWriter puts into the file for the
-- phone player's recommendations.

-- NULL: not measured yet, or the file has too little sound to say anything (then the version is set).
ALTER TABLE tracks ADD COLUMN audio_features TEXT;

-- AudioAnalysis.VERSION of the measurement; an older or missing version is measured again.
ALTER TABLE tracks ADD COLUMN audio_features_version INTEGER;
