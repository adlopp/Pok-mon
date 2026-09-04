# Ultra Yea Launcher

A small self-updating launcher for Windows, written in C# / .NET 8 (WinForms).
It keeps a game folder up to date from a GitHub Releases feed and then starts
the game.

It is the updater used by the **Pokémon Ultra Yea** fan project, but the update
logic is generic: point `launcher_config.json` at any public GitHub repository
that publishes release `.zip` files (optionally with a `manifest.json`) and it
works.

## What it does

On launch:

1. Reads `version.txt` next to `Launcher.exe` (the installed version).
2. Queries the repo's latest release
   (`https://api.github.com/repos/OWNER/REPO/releases/latest`).
3. If a `manifest.json` asset is present, uses it for the target version,
   SHA-256, release notes and a list of files to delete. Otherwise it falls
   back to the release tag and the first `.zip` asset.
4. If the remote version is higher: shows the changelog and an **Update & Play**
   button. Downloads the `.zip` with a progress bar, verifies the SHA-256,
   extracts it and copies the files over the game folder.
5. Writes the new `version.txt` and starts `Game.exe`.

Not touched by an update: `version.txt`, `launcher_config.json`, and save data
(which the game keeps under `%AppData%\Roaming`, outside the game folder).

If `Launcher.exe` itself changed, it is renamed to `Launcher.exe.old` (allowed
on Windows even while running), the new build is put in place, and the `.old`
file is removed on the next start.

## Download

Get `Launcher.exe` from the
[Releases page](https://github.com/adlopp/ultrayea-launcher/releases).

Windows release builds are code-signed for free by the
**[SignPath Foundation](https://signpath.org)**'s open-source code-signing
program. The signing runs in CI (see
[`.github/workflows/build-sign.yml`](.github/workflows/build-sign.yml)), so every
published `Launcher.exe` is built and signed straight from this repository.

## Security

- Runs as `asInvoker` — it never requests administrator rights
  (see [`app.manifest`](app.manifest)). The game folder must live in a
  user-writable location.
- Only ever writes inside the game folder, a temp folder
  (`%TEMP%\UltraYeaLauncher`) and its own executable. Path traversal in the
  `manifest.json` `delete` list is rejected.
- No registry access, no telemetry, no network access other than the GitHub
  API and the release asset download.

## Build

Requires the .NET 8 SDK:

```powershell
winget install Microsoft.DotNet.SDK.8
```

Then:

```powershell
.\build.ps1        # -> dist\Launcher.exe   (self-contained, single file, win-x64)
```

## Files

| File | Purpose |
|---|---|
| `UltraYeaLauncher.csproj` | Project. Publishes single-file, self-contained, win-x64. |
| `app.manifest` | `asInvoker` (never admin) + PerMonitorV2 DPI. |
| `Program.cs` | Entry point; cleans up `Launcher.exe.old`. |
| `LauncherConfig.cs` | Loads `launcher_config.json` (allows `//` comments and trailing commas). |
| `GitHubModels.cs` | GitHub API and `manifest.json` types. |
| `GitHubClient.cs` | Calls the Releases API. |
| `VersionUtil.cs` | Component-wise version comparison (`1.2.1.1` vs `1.2.2.0`). |
| `Updater.cs` | Download, verify, extract, apply, self-update. |
| `MainForm.cs` | UI. |
| `launcher_config.json` | Template shipped next to the game. Set `repoOwner` / `repoName`. |

## License

MIT — see [LICENSE](LICENSE).
