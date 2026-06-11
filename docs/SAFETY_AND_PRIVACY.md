# Safety and privacy

## What DeskRealm changes

DeskRealm changes the current user's Windows Desktop Known Folder path while it is running. This changes what Explorer displays as the Desktop.

It does not intentionally move, delete, copy, upload, or synchronize files.

## What DeskRealm stores

DeskRealm stores local files only:

```text
%APPDATA%\DeskRealm\deskrealm.config.json
%APPDATA%\DeskRealm\icon-layouts\*.json
%LOCALAPPDATA%\DeskRealm\logs\deskrealm.log
```

Icon layout files can contain:

```text
virtual desktop GUIDs
realm names
Desktop icon positions
Shell display/parsing identity metadata for icons
monitor topology metadata: resolution, bounds, orientation, DPI / scale
```

This data stays local and is used only to restore the Desktop view.

## Network behavior

DeskRealm has no network feature and no telemetry.

## OneDrive warning

DeskRealm rejects OneDrive Desktop paths by default because dynamic Desktop Known Folder switching can interact badly with folder synchronization.

You can disable this check manually, but that is not recommended unless you understand the risk.

## Display topology warning

DeskRealm tracks display topology to avoid corrupting icon layouts when monitors, resolution or DPI / scale change. It intentionally skips icon-layout saves while such changes are settling.

## Emergency restore

Run:

```powershell
.\scripts\Restore-Desktop.ps1
```

The script reads `%APPDATA%\DeskRealm\deskrealm.config.json` and restores `originalDesktopPath`.

## Backups

Before first use, back up important Desktop files. DeskRealm is designed not to move/delete files, but it intentionally changes a high-impact Windows Shell setting.
