# Fetches the programs the Android app ships: Python and ffmpeg (Termux builds, prebuilt by youtubedl-android),
# quickjs-ng (static build from its own release) and the yt-dlp zipapp. Every file is pinned by version and SHA-256.
# They are not committed; run this once before building Tapedeck.Android (CI does the same):
#   powershell -ExecutionPolicy Bypass -File tools/android-natives.ps1
# Executables go to Tapedeck.Android/native/arm64-v8a as lib*.so, because an app may only start programs from its
# native library folder. Their shared libraries stay inside the *.zip.so files, which the app unpacks on first start.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Tapedeck.Android'
$cache = Join-Path $project 'natives-cache'
$native = Join-Path $project 'native/arm64-v8a'
$assets = Join-Path $project 'Assets/tools'

$maven = 'https://repo1.maven.org/maven2/io/github/junkfood02/youtubedl-android'
$downloads = @(
    @{ Name = 'library-0.18.1.aar'; Url = "$maven/library/0.18.1/library-0.18.1.aar"
       Sha256 = '579b5fb480892b1abc2b218c2089699d52759cc8d7ba256bf876453f0365faef' },
    @{ Name = 'ffmpeg-0.18.1.aar'; Url = "$maven/ffmpeg/0.18.1/ffmpeg-0.18.1.aar"
       Sha256 = '0a87ffa6cf912b0fe76c1a99b9107f543ee2f247935fae2c71f0822eb7bc5f49' },
    @{ Name = 'qjs-linux-aarch64'; Url = 'https://github.com/quickjs-ng/quickjs/releases/download/v0.18.0/qjs-linux-aarch64'
       Sha256 = '2c005c7171424a598d31c53c8dc4a0ae2e4958eb6fbb37908c885a307c78187c' },
    @{ Name = 'yt-dlp'; Url = 'https://github.com/yt-dlp/yt-dlp/releases/download/2026.10.26/yt-dlp'
       Sha256 = 'e51e69e89489f199530f13d32a4342da3ddb2cf34842f364603a066db05afbf3' }
)

New-Item -ItemType Directory -Force $cache, $native, $assets | Out-Null

foreach ($file in $downloads) {
    $path = Join-Path $cache $file.Name
    if (-not (Test-Path $path)) {
        Write-Host "Downloading $($file.Name)"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $file.Url -OutFile "$path.part" -UseBasicParsing
        Move-Item "$path.part" $path
    }
    $hash = (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
    if ($hash -ne $file.Sha256) {
        Remove-Item $path
        throw "$($file.Name) has SHA-256 $hash instead of $($file.Sha256); deleted, nothing was installed."
    }
}

# Copies the arm64 libraries of an AAR into the native folder.
function Copy-FromAar([string] $aar, [string[]] $names) {
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $cache $aar))
    try {
        foreach ($name in $names) {
            $entry = $zip.GetEntry("jni/arm64-v8a/$name")
            if ($null -eq $entry) { throw "$name is missing in $aar" }
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $native $name), $true)
        }
    }
    finally {
        $zip.Dispose()
    }
}

Copy-FromAar 'library-0.18.1.aar' @('libpython.so', 'libpython.zip.so')
Copy-FromAar 'ffmpeg-0.18.1.aar' @('libffmpeg.so', 'libffmpeg.zip.so')
Copy-Item (Join-Path $cache 'qjs-linux-aarch64') (Join-Path $native 'libqjs.so') -Force
Copy-Item (Join-Path $cache 'yt-dlp') (Join-Path $assets 'yt-dlp') -Force

Write-Host "Natives ready in $native and $assets"
