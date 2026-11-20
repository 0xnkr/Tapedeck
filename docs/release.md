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
| `Tapedeck-1.2.3.apk`  | `release.yml` | The Android app (arm64), signed with the release key.                  |
| `Tapedeck-1.2.3.apk.sha256` | `release.yml` | SHA-256 checksum of the APK.                                     |
| `Tapedeck-1.2.3-macos-arm64.zip` | `release.yml` | `Tapedeck.app` for Macs with Apple silicon, signed ad hoc, not notarized ([macOS](macos.md)). |
| `Tapedeck-1.2.3-macos-x64.zip` | `release.yml` | `Tapedeck.app` for Intel Macs.                                  |
| `….zip.sha256`        | `release.yml` | SHA-256 checksums of the two zips.                                     |
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

The Android app gets the same version. Android compares a second number, the version code, to decide whether an APK is
an update: `Tapedeck.Android.csproj` derives it from the version as `MAJOR·10000 + MINOR·100 + PATCH` (`1.5.0` →
`10500`). A pre-release has the same code as its final version, so the final version installs over it. Local builds
have the code `1`.

> [!NOTE]
> No version or build number in the code is changed for a release. `Tapedeck.csproj` and `Tapedeck.Desktop.csproj` keep `<Version>0.0.0-dev</Version>`;
> `release.yml` overrides it with the tag's version (`-p:Version=1.1.0`), and the .NET SDK appends the git commit to the
> informational version (`1.1.0+2e02d19…`). `AppInfo` reads both, so the settings show `1.1.0 · 2e02d19` without any
> manual step.

## Android signing key (once)

Android installs an update only if it is signed with the same key as the installed app. The key is therefore created
once and kept forever; if it is lost, every user has to uninstall the app (and lose its database) to install a newer
version. Keep the `.keystore` file and its password in a password manager or another safe place, never in the
repository.

Create the key with `keytool` from the JDK (it asks for the password and the name fields):

```bash
keytool -genkeypair -v -keystore tapedeck-release.keystore -alias tapedeck -keyalg RSA -keysize 4096 -validity 36500
```

The workflow reads it from three repository secrets (GitHub → Settings → Secrets and variables → Actions):

| Secret                      | Content                                                        |
|-----------------------------|----------------------------------------------------------------|
| `ANDROID_KEYSTORE`          | The `.keystore` file as Base64 (see below).                    |
| `ANDROID_KEY_ALIAS`         | The alias, `tapedeck` in the command above.                    |
| `ANDROID_KEYSTORE_PASSWORD` | The password; `keytool` uses the same one for store and key.  |

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("tapedeck-release.keystore")) | Set-Clipboard
```

Without `ANDROID_KEYSTORE` the release stops before anything is published; an unsigned or debug-signed APK is never
released.

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
2. Android job: install the Android workload, fetch the bundled tools (`tools/android-natives.ps1`), build and sign
   `Tapedeck-<version>.apk`.
3. macOS job (arm64 and x64 on a macOS runner): publish, bundle and sign `Tapedeck.app` ad hoc
   (`tools/macos-bundle.sh`), run `Tapedeck --self-check` on the arm64 build, zip both.
4. Extract the release notes from the changelog.
5. Run the tests.
6. Build `Tapedeck.exe` with that version.
7. Compute the checksums and create the release with the `.exe`, the `.apk`, the two macOS zips and their `.sha256`
   files.

A run takes a few minutes; the Android job takes longest because of the workload install. Afterwards the release is listed under [Releases](https://github.com/0xnkr/Tapedeck/releases).

### 6. Verify the release

The checksum of the downloaded `.exe` has to match the content of `Tapedeck.exe.sha256`:

```powershell
(Get-FileHash .\Tapedeck.exe -Algorithm SHA256).Hash.ToLower()
```

The running application shows its version and build under **Settings → Tools → Tapedeck**, e.g. `1.1.0 · 2e02d19`.
The part after the dot is the git commit the `.exe` was built from.

For the APK, install it over the previous release on the phone. Android shows the version under the app's info
(Settings → Apps → Tapedeck), and Tapedeck's own settings show the same line as on the desktop. An error like "App not
installed" when updating means the APK was signed with a different key.

## Troubleshooting

**Failed workflow.** The log of the failed step names the cause. Common causes:

- *"has no section '## [1.1.0]'"*: the changelog section is missing or has a different heading.
- *"is no version like v1.2.3"*: the tag does not follow the version scheme.
- *"The secret ANDROID_KEYSTORE is missing"*: the signing secrets are not set, see
  [Android signing key](#android-signing-key-once).
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
`log.txt` from the app data folder (on Windows `%APPDATA%\Tapedeck\log.txt`); on Android it shares the file instead. The log records the start with version
and environment, downloads, imports, MusicBrainz results, written tags and all errors. Once it exceeds 2 MB, Tapedeck
trims it to the newest megabyte at start.
