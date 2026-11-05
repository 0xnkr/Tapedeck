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

- Track detail page with corrections for artist, title, album, album artist and year. Corrections save automatically while you type.
- Source chips show where each value comes from: your correction, YouTube or the parsed title.
- Genres and a five-star rating.
- "Write tags" puts title, artist, album, album artist, year, genres, the YouTube link and the cover into the audio file, and renames the file to match corrections.

#### Playlists

- Create, rename and delete playlists.
- Reorder tracks by drag and drop or with <kbd>Alt</kbd>+<kbd>↑</kbd>/<kbd>↓</kbd>.
- "Add to playlist" in the track menu and on the detail page.
- Every playlist is kept up to date as an `.m3u8` file in the library folder, ready for Android players like Poweramp or Musicolet. Your own `.m3u8` files are never touched.
- Import YouTube playlists from the link bar or with "From link". Imported playlists stay linked, and "Sync" adds new videos without removing or reordering anything. Mixes (`list=RD…`) are added as a single video.
- "Download playlist" downloads all pending and missing tracks of a playlist.

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
