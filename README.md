# DeskRealm v0.7.0

> **Current public release:** v0.7.0

DeskRealm makes Windows virtual desktops feel like real, visible workspaces. Give each desktop its own name, wallpaper, icon layout, hotkey, protection state and default-startup role — then control those settings directly from a single Realm Studio card instead of hunting through scattered Windows surfaces.

**Realm Studio is the v0.7.0 rework:** a modern WinUI 3 dashboard where each realm is both a clear visual summary and an instinctive control surface. Switch a workspace, preview or change its wallpaper, edit a hotkey inline, lock every saved layout, choose the default realm and manage variants without bouncing through a chain of settings dialogs. DeskRealm stays native to Windows: no overlay desktop, no fake workspace layer and no private virtual-desktop COM dependency.

## See Realm Studio in action

**Desktop realm switching and layout continuity**

![DeskRealm switching between Windows desktop realms while keeping each workspace organised](https://github.com/user-attachments/assets/80818a95-7ed6-40e8-915c-afb4475325f5)

**Realm Studio — direct wallpaper, hotkey, lock and default controls from each realm card**

![Realm Studio dashboard showing direct per-realm controls](https://github.com/user-attachments/assets/33d0f1e5-d6c0-4177-9074-ee8a20178d12)

## What DeskRealm gives you

- **A distinct space for every Windows virtual desktop:** each realm can use its own managed Desktop folder or the original Windows Desktop folder, with GUID-based identity that survives desktop reordering.
- **Per-workspace icon layouts:** DeskRealm saves and restores icon positions for each realm and each display topology, so a laptop setup and a multi-monitor setup can stay intentional.
- **Wallpapers that stay honest:** change a wallpaper from the Realm Studio preview, or let DeskRealm import a readable wallpaper change made directly in Windows.
- **Hotkeys without a settings maze:** assign or edit a realm hotkey directly on its card, with capture, reset, save and cancel actions where you need them.
- **Clear protection:** lock a whole realm or retain individual variant locks. The glyph always represents the current state, while the tooltip tells you the next action.
- **A real startup workspace:** mark one realm as the default, choose whether DeskRealm starts visible or quietly in the notification area, and keep this separate from Windows startup registration.
- **Safe Windows-name updates:** rename a virtual desktop and explicitly choose whether Task View applies the new name on next reboot or through an immediate Explorer restart.

## How it works

DeskRealm maps each Windows virtual-desktop GUID to a realm profile. It changes the current user’s Desktop Known Folder during a realm switch, uses Explorer-backed icon-layout persistence, and preserves the original physical Windows Desktop folder when that native realm is relabeled.

It does **not** move files during an ordinary realm switch. It does **not** send realm names, icon names, paths, wallpapers, configuration or telemetry to a remote service.

## Important safety boundary

DeskRealm changes the current user’s **Desktop Known Folder** so Explorer displays the folder linked to the active realm. Read [Safety and privacy](docs/SAFETY_AND_PRIVACY.md) before use, especially when OneDrive Desktop backup is enabled.

DeskRealm never renames, moves or remaps the physical original Windows Desktop folder from Realm Studio.

## Install and run

Use a release asset from the GitHub Releases page:

- **Portable ZIP:** extract it, then run `DeskRealm.App.exe`.
- **Install bundle:** extract it, review `Install-DeskRealm.ps1`, then run it from PowerShell.

For source development:

```powershell
.\scripts\Run-DeskRealm.ps1
```

For a portable self-contained build:

```powershell
.\scripts\Build-Release.ps1
.\dist\DeskRealm\DeskRealm.App.exe
```

The release flow deliberately stays boring:

1. clean generated output;
2. restore `win-x64` dependencies;
3. build Release;
4. publish a self-contained single-file `win-x64` app;
5. verify `DeskRealm.App.exe` exists.

## Documentation

- [Installation](docs/INSTALLATION.md)
- [Configuration](docs/CONFIGURATION.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Safety and privacy](docs/SAFETY_AND_PRIVACY.md)
- [Smoke test](SMOKE_TEST.md)
- [Release process](docs/RELEASE_PROCESS.md)
- [Release checklist](docs/GITHUB_RELEASE_CHECKLIST.md)
- [Technical audit](docs/TECHNICAL_AUDIT.md)
- [Roadmap](docs/ROADMAP.md)
- [Changelog](CHANGELOG.md)

## Development history

Completed local implementation TODOs and candidate notes are retained under [docs/history](docs/history/README.md). They are historical records only; they are not part of the build or release pipeline.
