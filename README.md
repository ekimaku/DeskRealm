# DeskRealm v0.5.9

**DeskRealm turns Windows virtual desktops into real, separated Desktop realms.**

Each Windows virtual desktop gets its own Desktop folder, its own icon layout, and optional direct hotkeys. Instead of every `Win + Tab` workspace showing the same Desktop clutter, DeskRealm redirects the current user's Windows Desktop Known Folder to the folder assigned to the active virtual desktop.

> Example: `Personal`, `Work`, `Dev`, `Music` and `Gaming` can each display their own Desktop icons and shortcuts.

## Status

DeskRealm is a personal open-source Windows utility. Version `v0.5.9` builds on the safe `v0.5.8` first-run association model and adds a real user-facing UX layer: onboarding, settings, hotkey capture, layout/realm locks and layout variant management.

DeskRealm intentionally touches the Windows Desktop Known Folder. Read the safety notes before running it, especially if your Desktop is synchronized by OneDrive.

## Highlights in v0.5.9

### Smoother first run

- New DeskRealm window available from the tray and by tray icon double-click.
- Branded DeskRealm `DR` icon embedded in the compiled `.exe`, tray notification icon and main window.
- The UI stays hidden during normal use, but opens automatically on fresh installs.
- First-run onboarding explains how DeskRealm works before the first automatic Desktop switch.
- The original Windows Desktop can be associated with one realm without moving files.
- If association is skipped, DeskRealm creates `DeskRealm - Original Desktop.lnk` shortcuts inside managed realms so the old Desktop remains easy to find.

### Modern tray-first UI

- Closing the window with the cross hides it back to the tray.
- **Quit DeskRealm** is the explicit app exit.
- Main tray actions are also available from the UI: refresh, sync names, save/restore icon layout, restore original Desktop, startup toggle, open realms/config/logs and quit.
- The old WinForms tab-strip look has been replaced with a dark/cyan DeskRealm shell, rounded cards, custom buttons, status pills and dark in-app chrome.

### Hotkey capture

- Hotkeys are captured directly from the UI field instead of typed as free-form text.
- Click a field, hold one or two modifiers (`Win`, `Ctrl`, `Alt`, `Shift`), then press one main key.
- Capture stops immediately on the first main key.
- Releasing only modifiers cancels capture and restores the previous value.
- Default desktop hotkeys now avoid `Win+Shift+W` and `Win+Shift+V`:
  - Desktop 1: `Win+Shift+X`
  - Desktop 2: `Win+Shift+C`
  - Desktop 3: `Win+Shift+B`
  - Desktop 4: `Win+Shift+N`

### Icon Layout management

- The **Icon Layout** view groups saved layouts by realm.
- Child rows represent saved display-topology `variants` from each icon-layout JSON file.
- Each variant row shows monitor working areas separately, using persisted `DisplayX.workingWidth` / `DisplayX.workingHeight` metadata.
- The primary display is marked with `✅`.
- Layouts, realms and exact layout variants can be locked from the UI.
- Locked layouts protect existing icon positions from automatic overwrite while still allowing newly added icons to be captured once.
- Manual overwrite of a locked layout requires explicit confirmation.
- Stale saved variants can be removed with the confirmation-gated **Delete** action from the UI. Deleting a variant removes DeskRealm metadata only; it never deletes Desktop files or icons.

### Clear pause semantics

- **Enable realm switching automation** now means exactly that.
- When disabled, DeskRealm does not switch realm folders automatically and ignores DeskRealm desktop hotkeys.
- Pausing automation does not delete assignments, realm folders, icons, layouts or files.

## Core features

- Per-virtual-desktop Desktop folders.
- Folder names synchronized from Windows Task View / `Win + Tab` names.
- Existing realm folders are renamed, not duplicated, when workspace names change.
- Per-realm Desktop icon layout save/restore.
- Display-topology-aware icon layouts for monitor, resolution, orientation and DPI / scale changes.
- Deferred and verified icon restore after switches.
- Shell identity fallback for repeated shortcuts/icons across realms.
- Global direct desktop hotkeys.
- Tray-first runtime with an optional onboarding/settings window.
- UI-controlled layout, realm and variant locks for protected icon positions.
- Branded executable, tray and main-window icon.
- Emergency restore script for the original Desktop path.

## Important safety note

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
4. On a fresh config, complete the first-run DeskRealm window before the first automatic switch.
5. Enable **Start with Windows** from the tray or UI if desired.

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

The compiled executable embeds the DeskRealm `DR` icon from `src\DeskRealm.App\Assets\DeskRealm.ico`; the tray icon and main window use the same embedded icon.

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

## Configuration

Config file:

```text
%APPDATA%\DeskRealm\deskrealm.config.json
```

Current config version: `10`.

Key v0.5.9 fields:

```json
{
  "version": 10,
  "enabled": true,
  "desktopHotkeysEnabled": true,
  "desktopHotkeys": {
    "1": "Win+Shift+X",
    "2": "Win+Shift+C",
    "3": "Win+Shift+B",
    "4": "Win+Shift+N"
  },
  "lockedIconLayouts": {},
  "lockedRealms": {},
  "lockedIconLayoutVariants": {}
}
```

See [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md).

## Documentation

- [`CHANGELOG.md`](CHANGELOG.md)
- [`docs/release-notes/v0.5.9.md`](docs/release-notes/v0.5.9.md)
- [`docs/patch-notes/PATCH_NOTES_v0_5_9.md`](docs/patch-notes/PATCH_NOTES_v0_5_9.md)
- [`docs/INSTALLATION.md`](docs/INSTALLATION.md)
- [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md)
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/SAFETY_AND_PRIVACY.md`](docs/SAFETY_AND_PRIVACY.md)
- [`docs/REFERENCES.md`](docs/REFERENCES.md)

## License

Apache License 2.0. See [`LICENSE`](LICENSE).

## Attribution / citation

DeskRealm is created by **Ayahua**. If this project helps or inspires your own work, citation is appreciated. See [`CITATION.cff`](CITATION.cff) and [`docs/ATTRIBUTION_GUIDE.md`](docs/ATTRIBUTION_GUIDE.md).
