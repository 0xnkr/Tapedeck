# Apple Music

Tapedeck's library is Opus, which YouTube delivers without re-encoding. Apple Music, iTunes and the iPhone cannot play
Opus. With the target system **Apple** (Settings → Target system; the default on a Mac), Tapedeck keeps an M4A copy of
every downloaded track in a folder of its own, next to the library:

```
~/Music/Tapedeck/           Opus library: what Tapedeck manages, and what goes to an Android phone
~/Music/Tapedeck (Apple)/   M4A copies and .m3u8 playlists for Apple Music
```

## What the copies contain

- AAC at 256 kbit/s in an MP4 container (`.m4a`), encoded once from the Opus file. On macOS Tapedeck uses Apple's own
  AAC encoder when FFmpeg offers it (`aac_at`), otherwise FFmpeg's.
- The same tags as the Opus file: title, artist, album artist, album, year, main genres, track and disc number,
  MusicBrainz IDs, cover and the YouTube link as the comment.
- The lyrics without time stamps, since Apple Music shows the text as it is.
- No loudness tag: Apple Music measures its own (Sound Check). No `.lrc` files.

The file names follow the Opus files (`Artist - Title.m4a`).

## When the copies are made

The export runs on its own in the background, but only once Tapedeck has nothing else to do: no download, no
MusicBrainz, Last.fm or lyrics lookup. That way a copy gets the finished values instead of being rewritten after
every step.

- A new download is encoded.
- New values (a correction, a MusicBrainz match, lyrics) only rewrite the tags of the existing copy.
- A renamed library file renames its copy.
- A removed track loses its copy.
- **Export now** in the settings or in the "Apple" pill of the library starts right away and retries failed tracks.

The pill in the library toolbar shows how many copies are up to date, e.g. `Apple: 412/450`; the track page shows the
state of its copy.

Tapedeck only ever changes or deletes the files it wrote itself; your own files in the folder stay.

## Into Apple Music

- **Mac:** In Music, choose **File → Add to Library** and pick the Apple folder. Import each playlist with
  **File → Library → Import Playlist** and its `.m3u8` file.
- **iPhone:** Add the files to Music on a Mac (or to the Apple Devices app or iTunes on Windows) and sync the phone.

Music copies the files into its own library by default, so a changed copy has to be added again. Turning off
**Settings → Files → Copy files to Music Media folder when adding to library** in Music makes it use the copies where
they are.
