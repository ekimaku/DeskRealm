# DeskRealm configuration

Config path:

```text
%APPDATA%\DeskRealm\deskrealm.config.json
```

After editing the file manually, use the tray action:

```text
Reload hotkeys from config
```

or restart DeskRealm for full reload.

## Main options

| Key | Default | Description |
|---|---:|---|
| `enabled` | `true` | Enables automatic desktop switching. |
| `pollIntervalMs` | `750` | How often DeskRealm checks the active virtual desktop. |
| `restoreDesktopOnExit` | `true` | Restores the original Desktop path when quitting. |
| `rejectOneDriveDesktop` | `true` | Refuses to operate if the original Desktop path appears to be under OneDrive. |
| `syncRealmNamesWithVirtualDesktopNames` | `true` | Renames/adopts realm folders based on Win+Tab desktop names. |
| `realmNameMaxLength` | `80` | Maximum sanitized realm folder name length. |
| `iconLayoutPersistenceEnabled` | `true` | Enables icon layout save/restore. |
| `iconLayoutSettleDelayMs` | `500` | Delay after Shell refresh before layout restore. Increase slightly if icons are restored too early. |
| `iconLayoutAutoSaveEnabled` | `false` | Compatibility setting from v0.5.0. Background polling is disabled in v0.5.1 and the recommended value is `false`. |
| `iconLayoutAutoSaveIntervalMs` | `60000` | Compatibility setting retained for old configs. |
| `desktopHotkeysEnabled` | `true` | Enables global hotkeys. |
| `desktopHotkeys` | see below | Maps virtual desktop number to hotkey string. |
| `hotkeyInitialDelayMs` | `180` | Delay after a global hotkey before sending navigation keystrokes. |
| `hotkeySwitchStepDelayMs` | `160` | Delay between each Win+Ctrl+Left/Right step. |
| `hotkeySwitchSettleTimeoutMs` | `3000` | Timeout while waiting for Windows registry state to confirm the target desktop. |
| `startWithWindows` | `false` | Updated by tray menu when startup is toggled. |


## Icon layout save model

Since v0.5.1, DeskRealm does not poll the Desktop icon layout every few seconds by default. Periodic Shell capture can briefly show the Windows busy cursor, so the quiet model is:

- save the active realm layout before switching away;
- restore the target realm layout after the Desktop folder switch;
- save the active realm layout before restoring the original Desktop on exit;
- keep manual tray actions for explicit save/restore.

`iconLayoutAutoSaveEnabled` still exists for config compatibility, but v0.5.1 does not rely on periodic background capture. The recommended value is `false`.

## Default hotkeys

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

## Supported hotkey syntax

Examples:

```text
Win+Shift+W
Ctrl+Alt+1
Win+Alt+F1
```

Supported modifier tokens:

```text
Win
Ctrl
Shift
Alt
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
- invalid current virtual desktop GUID: stop with explicit error.
