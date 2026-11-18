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

## Release packages

The GitHub Actions workflow at `.github/workflows/release.yml` builds x64 and
arm64 framework-dependent Windows packages. The release contains:

- `NitroPC-win-x64.zip`
- `NitroPC-win-arm64.zip`
- `SHA256SUMS.txt`

Each ZIP contains `NitroPC.exe`, the WPF/runtime files, and the four
`NitroPC.Updater` files. Releases are created from a semantic `vX.Y.Z` tag or
through the manual workflow with a version input.

The first release must be installed manually. Updates can replace an existing
installation only where the current Windows user can write the application
directory; the recommended location is a user-owned folder such as
`%LOCALAPPDATA%\Programs\NitroPC`.
