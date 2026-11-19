# NitroPC Automatic Updates

NitroPC follows the same user-controlled GitHub release pattern used by the nearby
FeedbackRecorder and NitroMac projects:

1. On startup, NitroPC optionally asks GitHub for the latest
   `magnuslandahl/NitroPC` release.
2. It selects the exact `NitroPC-win-x64.zip` or `NitroPC-win-arm64.zip` asset and
   requires one `SHA256SUMS.txt` asset with an exact entry for that filename.
3. The release metadata is bound to immutable GitHub asset IDs. Before install,
   NitroPC rechecks the release and refuses a changed or removed asset.
4. The package is downloaded as a `.part` file, checked for declared size,
   verified with SHA-256, and extracted with path and expanded-size validation.
5. NitroPC asks the user before installing, copies `NitroPC.Updater.exe` to a
   temporary location, exits, and lets the updater replace the install directory.
6. The updater restores the previous directory if replacement fails and then
   relaunches `NitroPC.exe`.

The app sends no GitHub token, signs users into no account, and requires no
production NuGet update framework. Automatic checks are enabled by default and
can be disabled in the toolbox. **Check updates** always remains available in
both the toolbox and tray menu.

## CI and automatic release workflows

- **`.github/workflows/ci.yml`** runs on every pull request and push to `main`,
  verifying formatting (`dotnet format --verify-no-changes`), Release build,
  unit tests, and `win-x64` / `win-arm64` bundle publishing.
- **`.github/workflows/release.yml`** runs **automatically** on pushes to `main`
  that touch `src/**`, `NitroPC.sln`, or `release.yml` (and can also be run
  manually via `workflow_dispatch` with a `patch` / `minor` / `major` bump or
  explicit version). It calculates the next semantic `vX.Y.Z` version from the
  highest existing release tag (or `<Version>` baseline), stamps `-p:Version`
  into the binaries, and publishes:
  - `NitroPC-win-x64.zip`
  - `NitroPC-win-arm64.zip`
  - `SHA256SUMS.txt`

Each ZIP contains `NitroPC.exe`, the WPF/runtime files, and the four
`NitroPC.Updater` files. `UpdateService` queries `/releases/latest` with
`Accept: application/vnd.github+json` and downloads `SHA256SUMS.txt` and the
selected ZIP from `/releases/assets/{id}` with `Accept: application/octet-stream`.

The first release must be installed manually. Updates can replace an existing
installation only where the current Windows user can write the application
directory; the recommended location is a user-owned folder such as
`%LOCALAPPDATA%\Programs\NitroPC`.
