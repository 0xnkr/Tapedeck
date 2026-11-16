# Publishing a release

Releases are built entirely by GitHub Actions. The local part consists of updating the changelog, a commit and a tag;
the [`release.yml`](../.github/workflows/release.yml) workflow builds and publishes everything else.

## Contents of a release

Each release appears under [Releases](https://github.com/0xnkr/Tapedeck/releases) as "Tapedeck 1.2.3", with the
matching changelog section as release notes and the following files:

| File                  | Source        | Purpose                                                                |
|-----------------------|---------------|------------------------------------------------------------------------|
| `Tapedeck.exe`        | `release.yml` | The application as a single self-contained file, without an installer. |
| `Tapedeck.exe.sha256` | `release.yml` | SHA-256 checksum of the `.exe` for verifying a download.               |
| Source code (zip)     | GitHub        | The source code at the tagged commit.                                  |
| Source code (tar.gz)  | GitHub        | The same source code as tar.gz.                                        |

GitHub attaches the two source archives to every release automatically.

## Versioning

Tapedeck follows [Semantic Versioning](https://semver.org) (`MAJOR.MINOR.PATCH`):

- **PATCH** (`1.0.1`): bug fixes only.
- **MINOR** (`1.1.0`): new features that leave existing behavior intact.
- **MAJOR** (`2.0.0`): changes to existing behavior that require users to adapt.
- **Pre-release** (`1.1.0-beta.1`): a hyphen in the version marks the GitHub release as a pre-release.

The version is taken from the git tag only. Local builds carry the version `0.0.0-dev` and are therefore never mistaken
for a release.

> [!NOTE]
> No version or build number in the code is changed for a release. `Tapedeck.csproj` keeps `<Version>0.0.0-dev</Version>`;
> `release.yml` overrides it with the tag's version (`-p:Version=1.1.0`), and the .NET SDK appends the git commit to the
> informational version (`1.1.0+2e02d19…`). `AppInfo` reads both, so the settings show `1.1.0 · 2e02d19` without any
> manual step.

## Procedure

### 1. Verify the state

```bash
git status
```

```bash
dotnet test
```

The working tree is clean and all tests pass. The [CI workflow](https://github.com/0xnkr/Tapedeck/actions/workflows/ci.yml)
is green for the latest commit on `main`.

### 2. Finalize the changelog

The `## [Unreleased]` section of `CHANGELOG.md` becomes the section of the new version. An empty `## [Unreleased]`
section remains above it:

```markdown
## [Unreleased]

## [1.1.0] - 2026-12-01

### Added
- …

### Fixed
- …
```

The link references at the end of the file are updated accordingly:

```markdown
[Unreleased]: https://github.com/0xnkr/Tapedeck/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/0xnkr/Tapedeck/compare/v1.0.0...v1.1.0
```

The heading has to read exactly `## [1.1.0]`, the version without the `v` prefix. The workflow uses this section as the
release notes and stops before publishing anything if the section is missing or empty. The section can be checked
locally beforehand; the generated `notes.md` is not committed:

```powershell
./.github/scripts/changelog-section.ps1 -Version 1.1.0 -OutFile notes.md
```

### 3. Commit

```bash
git commit -am "released 1.1.0"
```

### 4. Tag and push

```bash
git tag v1.1.0
```

```bash
git push origin main v1.1.0
```

> [!IMPORTANT]
> The tag needs the leading `v` (`v1.1.0`), otherwise the workflow does not start.

### 5. Workflow run

Pushing the tag starts [Actions → Release](https://github.com/0xnkr/Tapedeck/actions/workflows/release.yml),
which runs these steps:

1. Read the version from the tag and validate it.
2. Extract the release notes from the changelog.
3. Run the tests.
4. Build `Tapedeck.exe` with that version.
5. Compute the checksum and create the release with the `.exe` and the `.sha256` file.

A run takes a few minutes. Afterwards the release is listed under [Releases](https://github.com/0xnkr/Tapedeck/releases).

### 6. Verify the release

The checksum of the downloaded `.exe` has to match the content of `Tapedeck.exe.sha256`:

```powershell
(Get-FileHash .\Tapedeck.exe -Algorithm SHA256).Hash.ToLower()
```

The running application shows its version and build under **Settings → Tools → Tapedeck**, e.g. `1.1.0 · 2e02d19`.
The part after the dot is the git commit the `.exe` was built from.

## Troubleshooting

**Failed workflow.** The log of the failed step names the cause. Common causes:

- *"has no section '## [1.1.0]'"*: the changelog section is missing or has a different heading.
- *"is no version like v1.2.3"*: the tag does not follow the version scheme.
- Failing tests.

A failed run has not created a release. After the fix, the tag is moved to the corrected commit and pushed again:

```bash
git tag -f v1.1.0
```

```bash
git push origin main
```

```bash
git push -f origin v1.1.0
```

**Faulty release.** The release is deleted on GitHub (Releases → the release → Delete), then corrected as above with
the tag moved and force-pushed. If the faulty release may already have been downloaded, a new PATCH version is the
cleaner option.

**Wrong tag pushed**, e.g. `1.1.0` without the `v`. The tag is removed locally and on GitHub:

```bash
git tag -d 1.1.0
```

```bash
git push origin :refs/tags/1.1.0
```

## Bug reports

A bug report should contain the version from the settings and the log file. **Settings → Tools → Open log** opens
`log.txt` from the app data folder (on Windows `%APPDATA%\Tapedeck\log.txt`). The log records the start with version
and environment, downloads, imports, MusicBrainz results, written tags and all errors. Once it exceeds 2 MB, Tapedeck
trims it to the newest megabyte at start.
