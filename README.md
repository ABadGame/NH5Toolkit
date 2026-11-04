# NASCAR Heat 5 Toolkit

A simple Windows launcher / hub for **NASCAR Heat 5 modding tools**. One app that brings your modding tools together: launch them, download them, keep them up to date.

Built with C# / WinForms (.NET 10).

## Features

- **One place to launch everything** — sidebar navigation with a page per tool (Track Loader, Mod Manager, Mod Studio), each starting its own standalone executable.
- **One-click downloads** — for any tool with a published download URL, the hub downloads the package (`.zip` or a plain `.exe`), installs it into its own tools folder and configures it automatically.
- **Update checking** — fingerprints each tool's remote package (HTTP `ETag` / `Last-Modified` via a HEAD request, with a SHA-256 fallback) and only re-downloads when something actually changed.
- **Smart installation** — if a package contains a bundled setup program, the hub runs it for you; otherwise it replaces the portable files in place (after checking the tool isn't still running).
- **Changelog** — hub news plus each tool's GitHub release notes, fetched automatically from the GitHub Releases API. Release titles link straight to the release page.
- **Dark mode** — a toggle in the sidebar; the choice is remembered.

## Getting started

1. Download the latest release (or build from source — see below).
2. Open the hub and click **Launch** on a tool, or click **Locate .exe** if you already have it installed somewhere.
3. Tools without an .exe can be grabbed with the **Download** button when a download link is configured.

### Tool paths

Each tool's configured `.exe` path lives in `%APPDATA%\NH5Toolkit\config.json`, together with the saved theme and per-tool update fingerprints. Tools downloaded through the hub are installed to `%APPDATA%\NH5Toolkit\Tools\<tool>\`.

### Configuring download links

Download URLs are listed in [`Tools.cs`](Tools.cs) as `DownloadUrl` placeholders. Any direct link to a `.zip` or `.exe` works — **GitHub Releases asset URLs are recommended**, e.g.:

```
https://github.com/<user>/<repo>/releases/download/v1.0/NH5ModManager.zip
```

The hub also derives each tool's GitHub repository from that URL, which is what powers the automatic changelog. No extra configuration needed.

## Building from source

Requires the .NET 10 SDK with the Windows desktop workload.

```
dotnet build
```

Then run `NH5Toolkit.exe` from `bin\Debug\net10.0-windows\` (or publish with `dotnet publish -c Release`).

## Project layout

| File | Purpose |
| --- | --- |
| `MainForm.cs` | Hub shell: sidebar navigation + page area, theme toggle |
| `HomePage.cs` | Landing page: tool cards + changelog |
| `ToolPage.cs` | Per-tool page: launch, locate, download, check for update |
| `Theme.cs` | Central light/dark palette |
| `Tools.cs` | Tool registry — add a tool here (name, description, download URL) |
| `Launcher.cs` | Launches a tool's executable, with friendly error handling |
| `ToolDownloader.cs` | First-time download flow with progress dialog |
| `ToolUpdater.cs` | Update checks and in-place file replacement |
| `PackageInstaller.cs` | Shared install logic (setup detection vs. portable replacement) |
| `Changelog.cs` | Hand-written hub news shown on the home page |
| `GitHubReleases.cs` | Fetches release notes from the GitHub Releases API |
| `ToolkitConfig.cs` | JSON-backed settings (`config.json`) |

## Roadmap

- [ ] Fill in the real download URLs as tools release (placeholders in `Tools.cs`)
- [ ] Remember the last checked time per tool / notify on startup when updates exist
- [ ] The hub auto-detects installed tools where possible

---

*NASCAR Heat 5 Toolkit is a community tool and is not affiliated with, or endorsed by, the developers or publishers of NASCAR Heat 5. Use it at your own risk.*
