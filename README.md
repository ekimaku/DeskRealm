# DeskRealm v0.6.0

**DeskRealm turns Windows virtual desktops into real, separated Desktop realms.**

Each Windows virtual desktop gets its own Desktop folder, its own icon layout, and optional direct hotkeys. Instead of every `Win + Tab` workspace showing the same Desktop clutter, DeskRealm redirects the current user's Windows Desktop Known Folder to the folder assigned to the active virtual desktop.

> Example: `Personnal`, `Work`, `Dev`, `Music`, `Gaming` can each display their own Desktop icons and shortcuts.

![DeskRealm demo](https://github.com/user-attachments/assets/92279b78-2661-4f68-87e2-e7bf9da902c8)

## Status

`v0.6.0` is the current public release. It supersedes earlier `0.5.x` builds and is the recommended baseline for new installs and upgrades. Local runtime testing validated startup recovery, locked-variant save integrity and the parallel hotkey pipeline before publication.

DeskRealm intentionally touches the Windows Desktop Known Folder. Read the safety notes before running it, especially if your Desktop is synchronized by OneDrive.

## What v0.6.0 improves

The goal of `v0.6.0` is simple: DeskRealm should feel like the target Desktop realm is already ready when Windows finishes moving to the virtual desktop.

| Before | v0.6.0 behavior | User-visible gain |
|---|---|---|
| DeskRealm waited fixed amounts of time after a switch. | DeskRealm watches concrete Windows/Explorer state and moves on as soon as the required state is true. | Less artificial waiting and fewer delayed icon restores. |
| Direct hotkeys navigated first, then loaded the destination realm. | The destination realm is resolved immediately; after the source layout is saved, Windows navigation and target layout preparation run together. | The realm can converge while the Windows desktop animation is still happening. |
| Icon operations could start a separate worker process per operation. | One persistent STA worker handles icon save/restore commands for the session. | Less process-start overhead during switches and manual layout actions. |
| Explorer could be accepted too early while still showing old icons. | DeskRealm requires exact target realm membership before restore success is logged. | Fewer false “restored” states and fewer mixed-realm layouts. |
| Reboot/logon could leave the Known Folder already on a realm and skip restore. | The first process reconciliation restores the active realm once even if the folder already matches. | Saved icon layouts recover after forced close/reboot instead of relying on graceful shutdown. |
| Manual save on locked layouts could affect more than the active topology variant. | Manual save is scoped to the current display topology and integrity-checks all preserved variants. | Safer multi-monitor/topology variant management. |

## Highlights in v0.6.0

### Adaptive switching instead of fixed waits

- Virtual desktop changes are observed through Windows registry notifications instead of a periodic WinForms timer.
- Registry notification bursts are coalesced into one serialized reconciliation lane.
- DeskRealm hotkeys wait for the physical modifiers to be released, then start Windows navigation and target-realm loading/restoration in parallel. The destination GUID, realm path and topology variant are resolved before navigation; the final active GUID commits the transaction or triggers explicit compensation to the realm Windows actually reached.
- Realm switching, icon capture, icon restore and the matching main-window actions share one serialized background lane away from the WinForms UI thread.
- The Shell view is accepted only when it contains the exact target realm entries; partially transitioned Explorer views are rejected, including invalidated item positions during multi-desktop jumps.
- Icon positions are applied and verified adaptively instead of sleeping for fixed settle/retry delays. Explorer view changes during enumeration are treated as bounded transition state, not as permanent persistence failure.

### Persistent icon-layout worker

- One persistent STA worker handles icon capture and restore commands for the session.
- The worker protocol is strict JSON Lines over BOM-free UTF-8.
- Request/response GUIDs prevent stale or crossed responses from being accepted.
- Worker failures are explicit and disable icon persistence for the current session; realm switching remains available.

### Targeted Shell refresh

- DeskRealm sends a targeted Shell directory notification for the new realm.
- The old global `WM_SETTINGCHANGE` broadcast is not used.
- Explorer readiness and icon membership remain the actual proof that the transition completed.

### Preserved UX foundations

- Branded executable/tray/window icon.
- First-run onboarding and safe original Desktop association.
- Capture-based hotkey editor.
- Layout, realm and exact topology-variant locks.
- Manual save overwrites only the active display-topology variant; every non-current variant is integrity-checked and preserved.
- Multi-display variant details and confirmation-gated variant deletion.
- Updated README demo GIF and tray-first modern UI.

## Core features

- Per-virtual-desktop Desktop folders.
- Folder names synchronized from Windows Task View / `Win + Tab` names.
- Existing realm folders are renamed, not duplicated, when workspace names change.
- Per-realm Desktop icon layout save/restore.
- Display-topology-aware icon layouts for monitor, resolution, orientation and DPI / scale changes.
- Adaptive and verified icon restore after switches.
- First-launch reconciliation restores the saved layout even when Windows already left the Desktop Known Folder on the active realm after reboot or forced session termination.
- Shell identity fallback for repeated shortcuts/icons across realms.
- Global direct desktop hotkeys.
- Tray-first runtime with an onboarding/settings window.
- UI-controlled layout, realm and variant locks.
- Emergency restore script for the original Desktop path.

## Important safety note

DeskRealm changes the current user's Windows Desktop Known Folder path while it runs. This is powerful, but it is not a visual overlay. Before testing:

1. Make sure your Desktop is not synchronized by OneDrive, or keep `rejectOneDriveDesktop` enabled.
2. Keep a backup of important Desktop files.
3. Keep the emergency restore script available.
4. Disable Windows Desktop icon auto-arrange if you want icon positions to remain stable.

## Requirements

- Windows 10 or Windows 11.
- Explorer must be the shell rendering the Desktop.
- Release downloads are self-contained `win-x64` artifacts and require no separate .NET installation.
- Source builds require the .NET 10 SDK selected by `global.json`.

## Install / download

For normal users, use the GitHub Release artifacts instead of building manually.

### Portable ZIP

1. Download `DeskRealm-<version>-win-x64-portable.zip` from the latest release.
2. Extract it somewhere stable, for example `%LOCALAPPDATA%\Programs\DeskRealm`.
3. Run `DeskRealm.App.exe`.
4. On a fresh config, complete first-run setup before the first automatic switch.
5. Enable **Start with Windows** from the tray or UI if desired.

### Install bundle

The release workflow also produces `DeskRealm-<version>-win-x64-install-bundle.zip`:

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

```powershell
.\scripts\Restore-Desktop.ps1
```

The script reads `%APPDATA%\DeskRealm\deskrealm.config.json` and restores `originalDesktopPath`.

## Configuration

Config file:

```text
%APPDATA%\DeskRealm\deskrealm.config.json
```

Current config version: `11`.

Key v0.6.0 fields:

```json
{
  "version": 11,
  "enabled": true,
  "iconLayoutPersistenceEnabled": true,
  "iconLayoutWorkerTimeoutMs": 8000,
  "shellViewReadyTimeoutMs": 2500,
  "iconLayoutRestoreVerificationTimeoutMs": 1400,
  "hotkeyModifierReleaseTimeoutMs": 1200,
  "desktopStepConfirmationTimeoutMs": 1800,
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

The timeout fields are maximum guardrails. They do not impose normal fixed waits.

See [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md).

## Documentation

- [`CHANGELOG.md`](CHANGELOG.md)
- [`docs/release-notes/v0.6.0.md`](docs/release-notes/v0.6.0.md)
- [`docs/patch-notes/PATCH_NOTES_v0_6_0_PERFORMANCE_PIPELINE.md`](docs/patch-notes/PATCH_NOTES_v0_6_0_PERFORMANCE_PIPELINE.md)
- [`docs/INSTALLATION.md`](docs/INSTALLATION.md)
- [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md)
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/SAFETY_AND_PRIVACY.md`](docs/SAFETY_AND_PRIVACY.md)
- [`docs/TECHNICAL_AUDIT.md`](docs/TECHNICAL_AUDIT.md)
- [`docs/validation/v0.6.0-release-control.md`](docs/validation/v0.6.0-release-control.md)
- [`SMOKE_TEST.md`](SMOKE_TEST.md)

## License

Apache License 2.0. See [`LICENSE`](LICENSE).

## Attribution / citation

DeskRealm is created by **Ayahua**. See [`CITATION.cff`](CITATION.cff) and [`docs/ATTRIBUTION_GUIDE.md`](docs/ATTRIBUTION_GUIDE.md).
