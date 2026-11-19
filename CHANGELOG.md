# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.5.0] - 2026-11-19

### Added

- **Tapedeck for Android.** The phone gets its own Tapedeck: share a link from the YouTube or YouTube Music app, and the song lands tagged in `Music/Tapedeck`, where every player on the phone finds it. Everything the desktop app does with a library works there too: playlists with `.m3u8`, corrections, MusicBrainz, covers, lyrics, genres, loudness and the sound analysis. Only the Spotify import stays on the computer.
  - Install `Tapedeck-1.5.0.apk` from the release (Android 13 or newer, 64-bit ARM). The app asks once for access to all files, so it can write and rename the songs.
  - It brings its own yt-dlp, Python, FFmpeg and QuickJS-ng, and updates yt-dlp from the settings like the desktop app.
  - Downloads go on with the screen off, with a notification. New and changed songs are announced to Android, so players show them and their new tags right away.
  - The library is separate from the computer's: export it as CSV, copy the folder and import the CSV to move it. Files already there are taken over instead of downloaded again, and the computer's playlists copied along stay.
  - Changing the library folder uses Tapedeck's own folder browser, since Android's opens empty.
- **Update hint.** When a newer version is out, the settings show it under Tools next to Tapedeck's version, with a link to the download. Tapedeck asks GitHub once per start, when the settings are opened.

### Changed

- Adding a link is faster: the metadata no longer asks YouTube for the audio formats, which the download fetches anyway. About a second on the computer; on the phone, seconds instead of almost half a minute.
- The open source credits in the settings take as many columns as there is room for, and the license texts wrap to the width of the window.

### Fixed

- Each Tapedeck deletes only the playlist files it wrote itself. Playlists of another Tapedeck in the same folder (the computer's, copied to the phone) and your own stay.

## [1.5.0-beta.2] - 2026-11-19

### Fixed

- The phone app deleted the playlists copied from the computer into its library folder. Each Tapedeck now deletes only the playlist files it wrote itself; those of another Tapedeck and your own stay.

## [1.5.0-beta.1] - 2026-11-19

### Added

- **Tapedeck for Android.** The phone gets its own Tapedeck: share a link from the YouTube or YouTube Music app, and the song lands tagged in `Music/Tapedeck`, where every player on the phone finds it. Everything the desktop app does with a library works there too: playlists with `.m3u8`, corrections, MusicBrainz, covers, lyrics, genres, loudness and the sound analysis. Only the Spotify import stays on the computer.
  - Install `Tapedeck-<version>.apk` from the release (Android 13 or newer, 64-bit ARM). The app asks once for access to all files, so it can write and rename the songs.
  - It brings its own yt-dlp, Python, FFmpeg and QuickJS-ng, and updates yt-dlp from the settings like the desktop app.
  - Downloads go on with the screen off, with a notification. New and changed songs are announced to Android, so players show them and their new tags right away.
  - The library is separate from the computer's: export it as CSV, copy the folder and import the CSV to move it. Files already there are taken over instead of downloaded again.

### Changed

- Adding a link is faster: the metadata no longer asks YouTube for the audio formats, which the download fetches anyway. About a second on the computer; on the phone, seconds instead of almost half a minute.

## [1.4.1] - 2026-11-14

### Added

- **The sound of each song for recommendations.** Tapedeck now measures how a downloaded song sounds and writes it into the file as `TAPEDECK_AUDIO`, so a player on the phone can suggest songs that match the mood, not only the genre: a calm ballad after a calm ballad, a driving track after a driving one. It holds the tempo (BPM) and, each as an average and how much it varies over the song, brightness, noisiness, how busy the arrangement is, energy and dynamics, e.g. `v=1;bpm=123.4;bpmconf=0.62;centroid=2140/610;…`. Intro and outro are left out of the measurement, so verse and chorus decide.
  - Every new download is measured right away; older and adopted files are measured in the background, about a second per song. "Write tags (N)" puts the values into the files.
  - Like the other fields, it is a plain Vorbis comment that other players ignore.
  - All three fields, how the sound is measured and a reference recommender built on them are described in [docs/recommendations.md](docs/recommendations.md).
- `Tapedeck.exe --audio-report [count]` measures all songs that have no value yet and writes `audio-report.md` into the app data folder: how the values spread over your library, and for some songs the ones that sound most alike.

## [1.4.0] - 2026-11-13

### Added

- **Data for recommendations in the files.** Tapedeck now writes two extra fields into each `.opus` file, so a player on the phone knows more about a song than its genre and can suggest similar songs that fit your taste:
  - `TAPEDECK_TAGS` describes the sound of the song by its artist's Last.fm tags, each with a weight from 0 to 1, e.g. `synthwave=0.82;chill=0.40;female vocalists=0.31`. Up to 15 tags with a weight of at least 0.10, strongest first. Tags that say something about the listener rather than the music, like "seen live" or "favorites", are left out. Songs that share strong tags count as similar.
  - `TAPEDECK_HISTORY` tells how much you liked the song on Spotify, e.g. `plays=57;skips=3;last=2025-03-14`: real plays (at least 30 seconds, not ended with forward or back), songs skipped within the first 10 seconds, and the day you last heard it. Only the raw counts are written; how much old plays still count is left to the player, so a song you loved years ago need not crowd out what you listen to now.
  - Both are plain Vorbis comments next to the usual tags, so other players ignore them. A field without data is removed instead of written empty.
- Last.fm now also gives the tags of every artist in the library, not only the genres of tracks without any. They are fetched in the background with your Last.fm key, like the genres; "Fetch genres (N)" under **Settings → Tools** now also counts the tracks whose artist has no tags yet. An artist without tags of their own ("A feat. B") gets those of the main artist.
- "Use Spotify history for recommendations" on the Spotify page counts plays and early skips of your streaming history for every library song. A newer history replaces the counts completely, since every export contains the earlier years as well. Note that the counts end up in your files: whoever gets the files gets them, too. IP address, device and country in the history are never read.
- New tags or counts mark the files as outdated, so "Write tags (N)" in the library puts them into the files.
- Import a Spotify playlist by its link: paste it on the Spotify page, and its songs are searched on YouTube and checked just like the songs of your streaming history, then added as a playlist in the same order. Any public playlist works, Spotify's own included, and you need no Spotify account. Without an account, Spotify shows only the first 100 songs of a playlist; the page tells you when songs are missing.
- Select several tracks with Ctrl+click or Shift+click and remove them together: "Remove N tracks" in the library toolbar or the Delete key, with one confirmation and the choice to delete the files too. Tracks that are downloading are skipped, and a file that cannot be deleted does not stop the others. On a playlist page, the same selection takes tracks out of the playlist.

### Fixed

- A band with a slash in its name, like AC/DC, is no longer split into two artists. The Spotify search looked for "AC Thunderstruck", and MusicBrainz, Last.fm and LRCLIB were asked for "AC". Two artists separated by " / " with spaces are still told apart.

## [1.3.0] - 2026-11-12

### Added

- Lyrics from [LRCLIB](https://lrclib.net), a free lyrics database. Downloaded tracks are looked up in the background; "Write tags" puts the lyrics into the file and, when they are timed, also as a `.lrc` file next to the song, so phone players show the lines along with the music. Timed lyrics are only taken when they fit the file: a YouTube Music upload, or a video as long as the song usually is. A music video with an intro gets the lyrics without times. Switch it off or fetch the rest under **Settings → Tools**.
- The detail page shows the lyrics right below the metadata, and lets you paste or correct your own under "Edit". "Search again" asks LRCLIB anew, "Reset" brings LRCLIB's lyrics back. A `.lrc` file of your own next to a song is never touched.
- Timed lyrics follow the song while it plays: the line sung now in large type, the next one smaller below, fading over as the song goes on. Click a line to jump there; when the track is not playing, it starts.

- "Target player" under **Settings → Library** writes the tags for the phone player you use. "PixelPlayer" adds a ReplayGain volume level, since PixelPlayer misreads the Opus one and plays the tracks nearly silent with ReplayGain on. Switching back takes it out again: after a change, "Write tags (N)" updates every file.

### Changed

- The Spotify search reads each YouTube hit about a second faster and with fewer requests, since it skips the download formats it never uses. Age-restricted hits can now be read too.

### Fixed

- The Spotify search no longer stops with "YouTube is slowing down" when one of the YouTube hits is age-restricted. Such a video was mistaken for throttling, so every new run stopped at the same song. It is now skipped and the search goes on with the other hits.
- An error page from Last.fm no longer stops fetching genres; only that artist is asked again in the next run.

## [1.2.0] - 2026-11-09

### Added

- Main genres: the fine genres of a track ("G-Funk", "Deep House", "Dance-Pop") are mapped to a few stable main genres such as Hip-Hop, Electronic, Pop, Rock or K-Pop. The detail page shows the main genre that goes into the file.
- Smart playlists can pick tracks by main genre, next to the fine genres.
- Genres from [Last.fm](https://www.last.fm) for tracks MusicBrainz has none for, from the tags listeners gave the artist. Enter your own free API key under **Settings → Tools**; new tracks are asked after the MusicBrainz lookup, and "Fetch genres (N)" asks for all others. Only tags that map to a main genre are kept, so noise like "seen live" stays out.
- "Download pending", "Download missing" and "Download playlist" download several tracks at once, 3 by default, so a long list is done much sooner. Choose 1 to 4 under **Settings → Library → Downloads at once**. One toast counts the whole batch, and after five failures in a row the batch stops, since YouTube is then most likely refusing for a while.

### Changed

- "Write tags" puts only the main genres into the file instead of every fine genre, so a phone's genre list stays short. The fine genres stay in Tapedeck. After the update, files tagged before show up in "Write tags (N)" to get their main genres.

## [1.1.0] - 2026-11-08

### Added

#### Import from Spotify

- "From Spotify" on the playlists page imports your most played songs from your Spotify streaming history. Request the "Extended streaming history" in your Spotify account under privacy; Spotify sends it as a zip file. Choose the zip or the unpacked folder, the whole history back to your first year is read in seconds.
- A play counts when it ran for at least 30 seconds, as a stream on Spotify does, and was not ended with the skip or back button. The same song on the album, the single and the deluxe edition counts as one song, while remixes, live and sped-up versions stay apart.
- Choose the songs by their plays, for example "at least 10" or "at least 40, at most 49", and leave out songs you skipped more often than not. Songs already in your library are recognized.
- "Search on YouTube" looks up every song on YouTube Music and takes the plain release (the "Topic" upload) with the length you heard on Spotify, without an intro or a different edit. Results are kept, so a search can be stopped and continued later, also after a restart. When YouTube asks to slow down, the search pauses with a notice instead of failing.
- Songs without a sure match wait under "To check" with up to three suggestions, each with the reason it is not sure, a link to listen on YouTube, "Take" and "None of them".
- "Add N songs" puts the found songs into the library and creates a playlist with them and the ticked songs already there, most played first. "Select all" and "Deselect all" tick or untick a whole tab, so a series of imports like 50+, 40–49, 30–39 gives playlists without overlaps.
- While found songs are not added yet, a reminder stays above every page, so a search that ran for hours is not forgotten.
- The history stays on your computer: only artist and title of the chosen songs go to YouTube for the search, and the IP addresses, devices and countries in it are never read. Tapedeck stores the YouTube results and your choices, not your plays.

## [1.0.0] - 2026-11-07

### Added

#### Library

- Add tracks by pasting a YouTube link. Every common link format works, including `youtu.be`, Shorts, embeds and YouTube Music.
- Metadata comes from `yt-dlp -J`. When YouTube has no proper artist or title, Tapedeck splits the video title into `Artist - Title` and drops extras like "(Official Video)".
- Track table with thumbnails, search, status filters and a summary of downloaded, failed and missing tracks.
- Downloads as Opus straight from YouTube, without re-encoding. Downloads show progress and can be cancelled one by one or all at once.
- Missing files are detected, and "Download missing" fetches them again.
- Tracks can be removed, optionally together with their file.
- Files that are already in the library folder are taken over instead of downloaded twice. Tapedeck matches them by the YouTube link in their tags, the video ID in their name or their plain name.

#### Track details

- Track detail page with corrections for artist, title, album, album artist and year. Corrections save automatically while you type. The page scrolls when the window is too low.
- Go to the previous or next track right on the detail page, with the arrow buttons next to "12 / 144" or the arrow keys. It follows the list you came from: the library as filtered and searched, or a playlist in its order.
- Going back from the details keeps your place in the list and shows the track you looked at last.
- Source chips show where each value comes from: your correction, MusicBrainz, YouTube or the parsed title.
- Genres and a five-star rating.
- "Write tags" puts title, artist, album, album artist, year, genres, the YouTube link and the cover into the audio file, and renames the file to match corrections.
- "Write tags (N)" above the library does the same for every downloaded track whose tags are outdated, for example after matching with MusicBrainz. A file that fails does not stop the others.

#### MusicBrainz and covers

- Tracks are matched with [MusicBrainz](https://musicbrainz.org) for album, album artist, release year, track and disc number and genres. New tracks are looked up in the background after adding a link, a playlist or a CSV, which can be turned off in the settings.
- Square covers from the [Cover Art Archive](https://coverartarchive.org) replace the 16:9 thumbnail in the file.
- Tracks without such a cover get a square one cut from the middle of their YouTube thumbnail, which for Topic uploads is the album cover. A Cover Art Archive cover found later replaces it.
- The matching recognizes the song first and picks the release by rules: the studio album before soundtrack, EP, single and compilation, the original before deluxe editions and reissues. Music videos get the album they belong to, live versions, remixes and sped-up uploads are kept apart.
- Only certain matches are applied on their own. Uncertain ones show up as suggestions on the detail page and under the new "Suggestions" filter, ready to apply or decline.
- "Match all with MusicBrainz" looks up the whole library again. Existing matches stay when nothing certain is found and get their genres and cover refreshed.
- Genres come from the album, else the recording, else the artist, since singles rarely have genres of their own.
- "Write tags" also writes the track and disc number and the MusicBrainz IDs.

#### Duplicates

- The new "Duplicates" filter shows songs that are in the library more than once, for example as the music video and the Topic upload, next to each other. Tracks matched with MusicBrainz count as the same song only when they are the same recording.
- "Keep this one" in the row menu removes the other copies, optionally with their files. Their playlist places and rating go to the kept track, and syncing a YouTube playlist does not bring them back.

#### Even volume

- Every download is measured once with FFmpeg's EBU R 128 filter. "Write tags" puts the result into the file as the Opus track gain `R128_TRACK_GAIN`, so players on the phone play loud music videos and quiet Topic uploads equally loud.
- Tracks downloaded earlier, and files taken over from the library folder, are measured in the background. Afterwards "Write tags (N)" takes the gain to their files.
- The built-in player levels tracks the same way, so a loud music video and a quiet upload play equally loud in the app too. This can be switched off in the settings.

#### Listening

- Click the cover of a downloaded track to play it, click again to pause. Works in the library, on playlist pages and on the track's detail page; one track plays at a time.
- A player bar below the page shows cover, title, progress and length, with pause, a slider to jump anywhere and a close button. <kbd>Space</kbd> pauses and resumes.
- Playback stops on its own before a file is tagged, renamed, downloaded again, removed or moved, so the file is never locked.
- Opus is decoded with Concentus and played through OpenAL Soft, on Windows, Linux and macOS without installing anything.

#### Playlists

- Create, rename and delete playlists.
- Reorder tracks by drag and drop or with <kbd>Alt</kbd>+<kbd>↑</kbd>/<kbd>↓</kbd>.
- "Add to playlist" in the track menu and on the detail page.
- Every playlist is kept up to date as an `.m3u8` file in the library folder, ready for Android players like Poweramp or Musicolet. Your own `.m3u8` files are never touched.
- Import YouTube playlists from the link bar or with "From link". Imported playlists stay linked, and "Sync" adds new videos without removing or reordering anything. Mixes (`list=RD…`) are added as a single video.
- "Download playlist" downloads all pending and missing tracks of a playlist.
- Smart playlists fill themselves from rules on artist, title, album, album artist, genre, year, rating and when a track was added, with "all rules" or "one is enough", an order and an optional limit. The editor shows a live preview. They stay up to date when the library changes, are exported as `.m3u8` like every playlist and are left out of "Add to playlist".

#### Backup and moving

- CSV export and import of the whole library, including corrections, genres and ratings.
- CSV export of a single playlist as `Tapedeck-<name>.csv`. "From CSV" turns such a file back into a playlist.

#### App

- Settings for language (English, German), accent color and library folder. Changing the folder moves the existing files along safely.
- Tool overview with versions and a one-click yt-dlp update.
- yt-dlp, FFmpeg and Deno are downloaded automatically on first use.
- Custom window chrome, a spinning vinyl toast for running work, and a design system with a switchable accent color.
- Lists and pages glide smoothly with the mouse wheel. Thumbnails and covers load in the background, so long libraries scroll without stutter.
- Open source credits in the settings. Each license opens its full text in the app, offline, and the libraries bundled along with the others are listed too.
- MIT license, plus third-party notices for everything bundled into `Tapedeck.exe`.
- Publish profile for a single self-contained `Tapedeck.exe` on Windows x64.
- GitHub Actions run the tests on every push and publish `Tapedeck.exe` with its SHA-256 checksum as a GitHub release for every version tag, with the matching changelog section as release notes.
- The settings show Tapedeck's version and build (the git commit it was built from) under Tools.
- A log file, `log.txt` in the app data folder, records start, downloads, imports, MusicBrainz results, written tags, the audio output and every error. "Open log" in the settings opens it for a bug report. It is trimmed to its newest part once it grows beyond 2 MB.
- A guide to publishing a release, [docs/release.md](docs/release.md).

### Fixed

- Writing tags could leave a file that could not be tagged again ("Value cannot be null"). This happened when tags and cover together were an exact multiple of 255 bytes long. Such files are now repaired before and after writing tags.
- A space typed at the end of a field on the detail page, as in "28 " on the way to "28 Grams", was removed when the page saved and reloaded.

[Unreleased]: https://github.com/0xnkr/Tapedeck/compare/v1.5.0...HEAD
[1.5.0]: https://github.com/0xnkr/Tapedeck/compare/v1.4.1...v1.5.0
[1.5.0-beta.2]: https://github.com/0xnkr/Tapedeck/compare/v1.5.0-beta.1...v1.5.0-beta.2
[1.5.0-beta.1]: https://github.com/0xnkr/Tapedeck/compare/v1.4.1...v1.5.0-beta.1
[1.4.1]: https://github.com/0xnkr/Tapedeck/compare/v1.4.0...v1.4.1
[1.4.0]: https://github.com/0xnkr/Tapedeck/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/0xnkr/Tapedeck/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/0xnkr/Tapedeck/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/0xnkr/Tapedeck/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/0xnkr/Tapedeck/releases/tag/v1.0.0
