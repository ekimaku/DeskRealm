# DeskRealm v0.5.7

**DeskRealm turns Windows virtual desktops into real, separated Desktop realms.**

Each Windows virtual desktop gets its own Desktop folder, its own icon layout, and optional direct hotkeys. Instead of every `Win + Tab` workspace showing the same Desktop clutter, DeskRealm redirects the current user's Windows Desktop Known Folder to the folder assigned to the active virtual desktop.

> Example: `Personnal`, `Work`, `Dev`, `Music`, `Gaming` can each display their own Desktop icons and shortcuts.

## Status

DeskRealm is a personal open-source Windows utility. Version `v0.5.7` keeps the validated v0.5.6 icon-layout engine and adds a first-run Desktop import wizard for new installations.

DeskRealm intentionally touches the Windows Desktop Known Folder. Read the safety notes before running it, especially if your Desktop is synchronized by OneDrive.

## Highlights in v0.5.7

- Stable icon layouts across repeated shortcuts/icons on multiple realms.
- Display-topology-aware layouts: active monitors, resolution, orientation, virtual bounds and DPI / scale are part of the saved layout context.
- Deferred icon restore after desktop switches so Explorer has time to show the target realm before positions are applied.
- Verified restore with retry for icons that do not move on the first Shell placement pass.
- Shell identity fallback: exact PIDL matching is tried first, then human-readable Shell identity keys are used when Explorer exposes the same shortcut with a different PIDL.
- Background icon polling is disabled by default to avoid periodic busy-cursor flicker.
- First-run import wizard can move the existing Windows Desktop into a selected realm and save its current icon layout.

## Features

- Per-virtual-desktop Desktop folders.
- Folder names synchronized from the names shown in Windows Task View / `Win + Tab`.
- Existing realm folders are renamed, not duplicated, when the workspace name changes.
- Per-realm Desktop icon layout save/restore.
- First-run Desktop import wizard for clean onboarding on new installations.
- Per-display-topology icon layout variants for monitor, resolution, orientation and scale changes.
- Guarded icon saves to prevent cross-desktop contamination during fast switches or display changes.
- Configurable global hotkeys to jump to numbered desktops.
- Optional startup with Windows from the tray menu.
- Tray app with status, config, logs, restore, pause/resume, and manual sync actions.
- Strict error handling: no silent fallback, no implicit file migration, no hidden merge.

## Default hotkeys

```text
Win+Shift+W -> Desktop 1
Win+Shift+X -> Desktop 2
Win+Shift+C -> Desktop 3
Win+Shift+V -> Desktop 4
Win+Shift+B -> Desktop 5
Win+Shift+N -> Desktop 6
```

These are configurable in:

```text
%APPDATA%\DeskRealm\deskrealm.config.json
```

DeskRealm does not use an unofficial direct numbered virtual desktop switch API. It reads the current Windows virtual desktop order and sends repeated `Win+Ctrl+Left/Right` navigation steps until the target desktop is reached, then applies the Desktop folder/layout switch.

## How it works

DeskRealm combines several Windows mechanisms:

- reads Windows virtual desktop GUIDs/order/names from Explorer registry state;
- redirects the current user's Desktop Known Folder to the matching realm folder;
- requests a Shell refresh so Explorer redraws the Desktop;
- captures/restores Desktop icon positions through the supported Shell folder view API;
- stores icon layouts per virtual desktop and per display topology;
- avoids background Shell icon polling by default;
- registers global hotkeys with `RegisterHotKey`;
- optionally adds/removes an HKCU Run entry for startup.

## Icon layout model

Icon layouts are stored here:

```text
%APPDATA%\DeskRealm\icon-layouts\<virtual-desktop-guid>.json
```

Since v0.5.6, each file can contain multiple display-topology variants. This lets DeskRealm keep different valid layouts for situations such as:

```text
- two monitors active at normal resolution / scale
- one monitor temporarily disconnected or asleep
- a game changing resolution
- Windows display scale / DPI changed
- same shortcut visible on several realms at different positions
```

When saving/restoring icons, DeskRealm first matches icons by exact Shell PIDL-derived identity. If Explorer exposes the same visible shortcut with a changed PIDL, DeskRealm falls back to Shell display/parsing identity keys.


## First-run Desktop import

On a fresh install, DeskRealm can offer to import the current Windows Desktop before the first automatic realm switch. The wizard can:

- assign the current Desktop to a chosen virtual desktop realm;
- move existing Desktop files and shortcuts into that realm;
- save the currently visible icon positions as that realm's initial layout.

The import is intentionally strict: it skips DeskRealm's own realms root and `desktop.ini`, and it refuses target name conflicts instead of overwriting or merging files silently. Existing upgraded installs do not show this wizard unexpectedly.

## Default realm layout

On first run, DeskRealm stores the current Desktop path as the restore path and creates:

```text
%USERPROFILE%\Desktop\DeskRealm\
```

With name sync enabled, folders are named from Windows Task View:

```text
%USERPROFILE%\Desktop\DeskRealm\Personnal
%USERPROFILE%\Desktop\DeskRealm\Work
%USERPROFILE%\Desktop\DeskRealm\Dev
```

If name sync is disabled, DeskRealm keeps legacy `D1`, `D2`, `D3`, `D4` style names.

## Safety model

DeskRealm is intentionally strict:

- it does not move, delete, copy, or silently migrate existing Desktop files during normal operation; the first-run import wizard can move Desktop items only after explicit user confirmation;
- it refuses OneDrive Desktop redirection by default;
- it refuses duplicate virtual desktop names;
- it refuses folder rename conflicts instead of merging folders;
- it refuses invalid virtual desktop registry state;
- it refuses icon saves while the active Windows virtual desktop and known Desktop realm are out of sync;
- it refuses icon saves while display topology is settling;
- it disables icon persistence for the current session if the isolated Shell icon worker fails, but keeps the core Desktop switching alive;
- it provides an emergency restore script.

## Important warning

DeskRealm changes the current user's Windows Desktop Known Folder path while it runs. This is powerful, but it is not a casual visual overlay. Before testing:

1. Make sure your Desktop is not synchronized by OneDrive, or keep `rejectOneDriveDesktop` enabled.
2. Keep a backup of important Desktop files.
3. Keep the emergency restore script available.
4. Disable Windows Desktop icon auto-arrange if you want icon positions to remain stable.

## Requirements

- Windows 10 or Windows 11.
- Explorer must be the shell rendering the Desktop.
- For release downloads: no separate .NET install is required for the self-contained `win-x64` artifacts.
- For source builds: .NET 8 SDK.

## Install / download

For normal users, use the GitHub Release artifacts instead of building manually.

### Portable ZIP

1. Download `DeskRealm-<version>-win-x64-portable.zip` from the latest release.
2. Extract it somewhere stable, for example `%LOCALAPPDATA%\Programs\DeskRealm`.
3. Run `DeskRealm.App.exe`.
4. Enable **Démarrer avec Windows** from the tray menu if desired.

### Install bundle

The release workflow also produces `DeskRealm-<version>-win-x64-install-bundle.zip`, containing transparent PowerShell install/uninstall scripts:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-DeskRealm.ps1 -StartAfterInstall -StartWithWindows
```

See [`docs/INSTALLATION.md`](docs/INSTALLATION.md).

## Build from source

From the repository root on Windows:

```powershell
.\scripts\Build-Release.ps1
```

Output:

```text
.\dist\DeskRealm\DeskRealm.App.exe
```

## Run from source

```powershell
.\scripts\Run-DeskRealm.ps1
```

## Emergency restore

If you ever need to restore the original Desktop path without launching DeskRealm:

```powershell
.\scripts\Restore-Desktop.ps1
```

The script reads:

```text
%APPDATA%\DeskRealm\deskrealm.config.json
```

and restores `originalDesktopPath`.

## Tray menu

- Status
- Refresh now
- Sync names now
- Save icon layout now
- Restore icon layout now
- Reload hotkeys from config
- Pause / Resume
- Démarrer avec Windows
- Open realms
- Open config
- Open logs
- Restore original Desktop
- Quit

## Configuration

Config file:

```text
%APPDATA%\DeskRealm\deskrealm.config.json
```

Current config version: `5`.

Example:

```json
{
  "version": 5,
  "enabled": true,
  "pollIntervalMs": 750,
  "restoreDesktopOnExit": true,
  "rejectOneDriveDesktop": true,
  "syncRealmNamesWithVirtualDesktopNames": true,
  "initialDesktopImportPromptEnabled": true,
  "initialDesktopImportPromptCompleted": false,
  "initialDesktopImportMoveFiles": true,
  "initialDesktopImportSaveLayout": true,
  "realmNameMaxLength": 80,
  "iconLayoutPersistenceEnabled": true,
  "iconLayoutSettleDelayMs": 500,
  "iconLayoutAutoSaveEnabled": false,
  "iconLayoutAutoSaveIntervalMs": 60000,
  "iconLayoutWorkerTimeoutMs": 8000,
  "iconLayoutDisplayTopologyGuardEnabled": true,
  "iconLayoutDisplayTopologySettleDelayMs": 1200,
  "iconLayoutSwitchRestoreDelayMs": 1400,
  "iconLayoutRestoreRetryCount": 2,
  "iconLayoutRestoreRetryDelayMs": 450,
  "desktopHotkeysEnabled": true,
  "desktopHotkeys": {
    "1": "Win+Shift+W",
    "2": "Win+Shift+X",
    "3": "Win+Shift+C",
    "4": "Win+Shift+V",
    "5": "Win+Shift+B",
    "6": "Win+Shift+N"
  },
  "hotkeyInitialDelayMs": 180,
  "hotkeySwitchStepDelayMs": 160,
  "hotkeySwitchSettleTimeoutMs": 3000,
  "startWithWindows": false,
  "originalDesktopPath": "C:\\Users\\<you>\\Desktop",
  "realmsRoot": "C:\\Users\\<you>\\Desktop\\DeskRealm",
  "nextRealmNumber": 5,
  "assignments": {
    "{00000000-0000-0000-0000-000000000000}": "Personnal"
  }
}
```

See [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md).

## Documentation

- [`docs/INSTALLATION.md`](docs/INSTALLATION.md)
- [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md)
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/SAFETY_AND_PRIVACY.md`](docs/SAFETY_AND_PRIVACY.md)
- [`docs/REFERENCES.md`](docs/REFERENCES.md)

## License

Apache License 2.0. See [`LICENSE`](LICENSE).

## Attribution / citation

DeskRealm is created by **Ayahua**. If this project helps or inspires your own work, citation is appreciated. See [`CITATION.cff`](CITATION.cff) and [`docs/ATTRIBUTION_GUIDE.md`](docs/ATTRIBUTION_GUIDE.md).
