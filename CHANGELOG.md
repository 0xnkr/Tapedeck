# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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

[Unreleased]: https://github.com/nilskruthoff/Tapedeck/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/nilskruthoff/Tapedeck/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/nilskruthoff/Tapedeck/releases/tag/v1.0.0
