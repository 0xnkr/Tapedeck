# Writes the section of one version from CHANGELOG.md (Keep a Changelog format) to a file, for the release notes.
# Fails when the section is missing or empty, so a release never goes out without notes.
#   ./.github/scripts/changelog-section.ps1 -Version 0.1.0 -OutFile release-notes.md
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $OutFile,
    [string] $Changelog = 'CHANGELOG.md'
)

$lines = Get-Content -Path $Changelog -Encoding utf8
$heading = '^## \[' + [regex]::Escape($Version) + '\]'
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $heading) { $start = $i + 1; break }
}
if ($start -lt 0) {
    Write-Error "$Changelog has no section '## [$Version]'. Rename '## [Unreleased]' before tagging."
    exit 1
}

$end = $lines.Count
for ($i = $start; $i -lt $lines.Count; $i++) {
    # the next version, or the link references at the bottom
    if ($lines[$i] -match '^## \[' -or $lines[$i] -match '^\[[^\]]+\]:\s') { $end = $i; break }
}

$section = if ($end -gt $start) { ($lines[$start..($end - 1)] -join "`n").Trim() } else { '' }
if ($section.Length -eq 0) {
    Write-Error "The section '## [$Version]' in $Changelog is empty."
    exit 1
}

Set-Content -Path $OutFile -Value $section -Encoding utf8
Write-Host "Release notes: $($section.Split("`n").Count) lines from '## [$Version]'"
