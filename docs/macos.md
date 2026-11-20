# Tapedeck on macOS

Tapedeck runs on macOS 12 or newer. Each release has two downloads:

| File                                | For                                   |
|-------------------------------------|---------------------------------------|
| `Tapedeck-<version>-macos-arm64.zip` | Macs with Apple silicon (M1 and newer) |
| `Tapedeck-<version>-macos-x64.zip`   | Intel Macs                            |

Unzip it and move `Tapedeck.app` to **Applications**.

## First launch

The app is signed ad hoc but not notarized by Apple (that needs a paid developer account). macOS therefore refuses to
open it the first time:

1. Open `Tapedeck.app`. macOS says it cannot verify the developer; click **Done**.
2. Open **System Settings → Privacy & Security**, scroll down to the note about Tapedeck and click **Open Anyway**.
3. Confirm once more. From then on Tapedeck opens normally.

Alternatively, remove the quarantine flag in the Terminal:

```bash
xattr -dr com.apple.quarantine /Applications/Tapedeck.app
```

## Where Tapedeck keeps its files

| What                      | Where                                          |
|---------------------------|------------------------------------------------|
| Database, settings, log, tools | `~/Library/Application Support/Tapedeck`  |
| The Opus library          | `~/Music/Tapedeck` (changeable in the settings) |
| The copies for Apple Music | `~/Music/Tapedeck (Apple)` (changeable)       |

## Tools

Like on Windows, Tapedeck downloads yt-dlp, FFmpeg and Deno on first use. The FFmpeg build for macOS is an Intel
build; on Apple silicon it runs through Rosetta 2, which macOS offers to install the first time it is needed.

## Apple Music

On a Mac the target system is **Apple** by default: next to the Opus library Tapedeck keeps an M4A copy (AAC,
256 kbit/s) of every downloaded track, with tags, cover and lyrics. See [Apple Music](apple.md).

## Testing without a Mac

Nobody on the team has a Mac. The [CI workflow](../.github/workflows/ci.yml) builds, tests and bundles the app on a
macOS runner and runs `Tapedeck --self-check`: it downloads the tools, loads OpenAL, measures the loudness of a test
tone and writes and reads back a tagged `.m4a`. It also tries to take a screenshot of the window, uploaded as an
artifact. The window itself (title bar, menus, playback) still needs a check on a real Mac.
