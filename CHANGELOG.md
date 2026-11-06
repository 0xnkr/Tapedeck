# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
- Source chips show where each value comes from: your correction, MusicBrainz, YouTube or the parsed title.
- Genres and a five-star rating.
- "Write tags" puts title, artist, album, album artist, year, genres, the YouTube link and the cover into the audio file, and renames the file to match corrections.
- "Write tags (N)" above the library does the same for every downloaded track whose tags are outdated, for example after matching with MusicBrainz. A file that fails does not stop the others.

#### MusicBrainz and covers

- Tracks are matched with [MusicBrainz](https://musicbrainz.org) for album, album artist, release year, track and disc number and genres. New tracks are looked up in the background after adding a link, a playlist or a CSV, which can be turned off in the settings.
- Square covers from the [Cover Art Archive](https://coverartarchive.org) replace the 16:9 thumbnail in the file.
- The matching recognizes the song first and picks the release by rules: the studio album before soundtrack, EP, single and compilation, the original before deluxe editions and reissues. Music videos get the album they belong to, live versions, remixes and sped-up uploads are kept apart.
- Only certain matches are applied on their own. Uncertain ones show up as suggestions on the detail page and under the new "Suggestions" filter, ready to apply or decline.
- "Match all with MusicBrainz" looks up the whole library again. Existing matches stay when nothing certain is found and get their genres and cover refreshed.
- Genres come from the album, else the recording, else the artist, since singles rarely have genres of their own.
- "Write tags" also writes the track and disc number and the MusicBrainz IDs.

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
- Open source credits in the settings. Each license opens its full text in the app, offline, and the libraries bundled along with the others are listed too.
- MIT license, plus third-party notices for everything bundled into `Tapedeck.exe`.
- Publish profile for a single self-contained `Tapedeck.exe` on Windows x64.
