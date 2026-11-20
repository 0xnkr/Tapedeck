#!/usr/bin/env bash
# Turns the output of `dotnet publish Tapedeck.Desktop -p:PublishProfile=osx-<arch>` into Tapedeck.app and zips it.
# Runs on macOS only (sips, iconutil, codesign, ditto); CI calls it on the macOS runners.
#
#   tools/macos-bundle.sh <publish dir> <arch> <version> <out dir>
#   tools/macos-bundle.sh publish/osx-arm64 arm64 1.6.0 publish
#
# Result: <out dir>/Tapedeck.app and <out dir>/Tapedeck-<version>-macos-<arch>.zip
# The app is signed ad hoc, not notarized: on first launch macOS asks the user to allow it (docs/macos.md).
set -euo pipefail

if [ "$#" -ne 4 ]; then
  echo "usage: $0 <publish dir> <arch> <version> <out dir>" >&2
  exit 1
fi

publish="$1"
arch="$2"
version="$3"
out="$4"
root="$(cd "$(dirname "$0")/.." && pwd)"
assets="$root/Tapedeck.Desktop/macos"

if [ ! -x "$publish/Tapedeck" ]; then
  echo "No Tapedeck executable in $publish" >&2
  exit 1
fi

# macOS wants a plain number like 1.6.0 in the bundle; a suffix such as -beta.1 stays in the zip's name
numeric="${version%%-*}"
numeric="${numeric%%+*}"

app="$out/Tapedeck.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish/." "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/Tapedeck"
sed "s/__VERSION__/$numeric/g" "$assets/Info.plist" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"

# the icon: SVG to a 1024 px PNG, then every size of the iconset
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
if ! command -v rsvg-convert >/dev/null 2>&1; then
  brew install librsvg >/dev/null
fi
rsvg-convert -w 1024 -h 1024 "$assets/tapedeck-icon.svg" -o "$work/icon-1024.png"
iconset="$work/tapedeck.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$work/icon-1024.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$work/icon-1024.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/tapedeck.icns"

# ad hoc: no developer account, but Apple Silicon refuses to run unsigned code at all
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"

zip="$out/Tapedeck-$version-macos-$arch.zip"
rm -f "$zip"
# ditto keeps the executable bits and the signature
ditto -c -k --keepParent "$app" "$zip"
echo "Built $zip"
