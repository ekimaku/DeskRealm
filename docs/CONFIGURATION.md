# Configuration

DeskRealm stores its user configuration here:

```text
%APPDATA%\DeskRealm\deskrealm.config.json
```

Current config version: `5`.

## Main settings

| Setting | Default | Purpose |
|---|---:|---|
| `enabled` | `true` | Enables Desktop realm switching. |
| `pollIntervalMs` | `750` | Main virtual desktop/watch loop interval. Minimum strict value: `250`. |
| `restoreDesktopOnExit` | `true` | Restores the original Desktop Known Folder path when DeskRealm exits. |
| `rejectOneDriveDesktop` | `true` | Refuses OneDrive Desktop paths by default. |
| `syncRealmNamesWithVirtualDesktopNames` | `true` | Uses Windows Task View names as realm folder names. |
| `initialDesktopImportPromptEnabled` | `true` | Enables the first-run Desktop import wizard for new configs. |
| `initialDesktopImportPromptCompleted` | `false` for new configs, migrated to `true` for upgrades | Prevents the wizard from interrupting existing users after upgrade. |
| `initialDesktopImportMoveFiles` | `true` | Default wizard option to move current Desktop items into the selected realm. |
| `initialDesktopImportSaveLayout` | `true` | Default wizard option to save current icon positions as the selected realm layout. |
| `realmNameMaxLength` | `80` | Maximum sanitized realm folder name length. |
| `startWithWindows` | `false` | Tray-controlled HKCU Run startup setting. |

## Icon layout settings

| Setting | Default | Purpose |
|---|---:|---|
| `iconLayoutPersistenceEnabled` | `true` | Enables icon layout capture/restore. |
| `iconLayoutSettleDelayMs` | `500` | Delay before capturing/restoring icon positions after Shell refresh. |
| `iconLayoutAutoSaveEnabled` | `false` | Compatibility setting. Background icon polling is disabled by default. |
| `iconLayoutAutoSaveIntervalMs` | `60000` | Legacy/compatibility interval for periodic autosave. Recommended value: keep default. |
| `iconLayoutWorkerTimeoutMs` | `8000` | Timeout for isolated Shell icon worker operations. |
| `iconLayoutDisplayTopologyGuardEnabled` | `true` | Refuses saves while display topology is changing/settling. |
| `iconLayoutDisplayTopologySettleDelayMs` | `1200` | Wait time after monitor/resolution/DPI changes before restoring. |
| `iconLayoutSwitchRestoreDelayMs` | `1400` | Wait time after virtual desktop/folder switch before restoring icons. |
| `iconLayoutRestoreRetryCount` | `2` | Number of restore attempts after a switch. Strict range: `1` to `5`. |
| `iconLayoutRestoreRetryDelayMs` | `450` | Delay between restore retry attempts. |

## First-run Desktop import settings

Since v0.5.7, new configurations can show a first-run wizard before DeskRealm redirects the Desktop to a realm. The wizard can import the currently visible Windows Desktop into a selected virtual desktop realm.

Relevant settings:

```json
{
  "initialDesktopImportPromptEnabled": true,
  "initialDesktopImportPromptCompleted": false,
  "initialDesktopImportMoveFiles": true,
  "initialDesktopImportSaveLayout": true
}
```

Upgrade behavior is intentionally conservative: existing configs migrated to version `5` are marked with `initialDesktopImportPromptCompleted: true`, so users who already run DeskRealm are not surprised by an onboarding prompt after an update.

The import operation is strict. It skips `desktop.ini` and DeskRealm's own realms root, and it refuses target filename conflicts instead of silently overwriting or merging files.

## Quiet icon persistence model

Since v0.5.1, DeskRealm does not poll icon positions every few seconds by default. Periodic Shell capture can briefly show the Windows busy cursor, so the quiet model is:

```text
manual save
save before switching away from a realm
save before restoring the original Desktop path on exit
restore after switching into a realm
```

## Display-topology-aware layouts

Since v0.5.3, layouts are separated by display topology. A topology includes:

```text
active monitors
virtual desktop bounds
monitor bounds / working areas
primary monitor
resolution
orientation
effective DPI / scale percentage
```

This prevents layouts from being overwritten when Windows temporarily changes the desktop view, for example:

```text
main monitor sleeps but secondary monitor stays active
a game changes resolution
Windows display scale changes
monitor orientation changes
```

When a topology changes, DeskRealm temporarily refuses icon layout saves, waits for the topology to settle, then restores the current realm using the best available variant.

## Fast-switch stabilization

Since v0.5.4, DeskRealm defers icon layout restore after switching Desktop folders. This prevents the following contamination case:

```text
previous realm icons are still visible
Windows virtual desktop has already changed
DeskRealm saves/restores too early
positions from one realm contaminate another
```

Recommended defaults:

```json
{
  "iconLayoutSwitchRestoreDelayMs": 1400,
  "iconLayoutRestoreRetryCount": 2,
  "iconLayoutRestoreRetryDelayMs": 450
}
```

## Repeated icon identity fallback

Since v0.5.6, saved icon entries include additional Shell identity metadata:

```json
{
  "itemKey": "pidl-sha256:...",
  "displayName": "Opera Browser",
  "shellDisplayName": "Opera Browser",
  "shellParsingName": "...",
  "identityKeys": [
    "pidl-sha256:...",
    "shell-display:opera browser",
    "shell-parsing:..."
  ]
}
```

Restore order:

1. exact PIDL-derived key match;
2. Shell display/parsing/name fallback match;
3. warning log if the saved icon still cannot be found in the current Desktop view.

After upgrading from older versions, use **Save icon layout now** once per important realm to refresh layouts with the new identity metadata.

## Hotkeys

Default hotkeys:

```json
"desktopHotkeys": {
  "1": "Win+Shift+W",
  "2": "Win+Shift+X",
  "3": "Win+Shift+C",
  "4": "Win+Shift+V",
  "5": "Win+Shift+B",
  "6": "Win+Shift+N"
}
```

Supported modifier tokens:

```text
Win
Ctrl
Shift
Alt
```

Examples:

```text
Win+Shift+W
Ctrl+Alt+1
Win+Alt+F1
```

DeskRealm does not silently replace rejected shortcuts. If a combination is already used by Windows or another app, the failure is logged and that binding is skipped.

## Assignments

`assignments` maps virtual desktop GUIDs to realm folder names.

When name sync is enabled, DeskRealm updates these mappings from the virtual desktop names shown in Windows Task View.

## Conflict policy

DeskRealm is strict:

- source folder exists, target does not: rename source to target;
- source missing, target exists: adopt target and update config;
- source and target both exist: stop with explicit conflict;
- duplicate virtual desktop names: stop with explicit conflict;
- invalid current virtual desktop GUID: stop with explicit error;
- known Desktop path and current Windows virtual desktop mismatch: skip/refuse icon layout save.
