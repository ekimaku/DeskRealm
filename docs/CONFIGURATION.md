# Configuration

DeskRealm stores its user configuration locally at:

```text
%APPDATA%\DeskRealm\deskrealm.config.json
```

Current config version: `10`.

## Core fields

| Field | Purpose |
|---|---|
| `version` | Config schema version. Current value: `10`. |
| `enabled` | Enables realm switching automation. When `false`, automatic switching and DeskRealm desktop hotkeys are paused. |
| `pollIntervalMs` | Watch-loop interval. Strict minimum: `250`. |
| `restoreDesktopOnExit` | Restores `originalDesktopPath` when DeskRealm exits. |
| `rejectOneDriveDesktop` | Refuses OneDrive Desktop paths by default. |
| `syncRealmNamesWithVirtualDesktopNames` | Keeps realm folder names aligned with Windows virtual desktop names. |
| `originalDesktopPath` | The Desktop path captured when the config was created. |
| `realmsRoot` | Root folder that contains managed realm folders. |
| `assignments` | Maps virtual desktop GUIDs to managed realm folders or explicit absolute paths. |

## First-run onboarding fields

| Field | Purpose |
|---|---|
| `initialDesktopImportPromptEnabled` | Allows the first-run onboarding UI on fresh configs. |
| `initialDesktopImportPromptCompleted` | Prevents onboarding from repeating once the user has decided. |
| `initialDesktopImportMoveFiles` | Legacy safety flag. Since `v0.5.8`, this remains `false`; onboarding does not move Desktop files. |
| `initialDesktopImportSaveLayout` | Saves the currently visible icon positions when the original Desktop is associated with a realm. |

On fresh `v0.5.9` configs, DeskRealm opens the main UI before the first automatic Desktop switch. The user can associate the original Desktop path with one realm or skip association and create shortcuts back to the original Desktop.

## Hotkeys

```json
"desktopHotkeysEnabled": true,
"desktopHotkeys": {
  "1": "Win+Shift+X",
  "2": "Win+Shift+C",
  "3": "Win+Shift+B",
  "4": "Win+Shift+N"
}
```

The `v0.5.9` defaults intentionally avoid `Win+Shift+W` and `Win+Shift+V`. Existing customized hotkeys are preserved during migration; only untouched legacy defaults are replaced.

The UI hotkey editor is capture-based:

1. click a hotkey field;
2. hold one or two modifier keys: `Win`, `Ctrl`, `Alt`, `Shift`;
3. press one main key;
4. click **Save + reload** to persist and re-register the global hotkeys.

Capture stops as soon as the first non-modifier key is pressed. If the user releases all modifiers before pressing a main key, capture is cancelled and the previous value is restored.

Hotkeys must contain one or two modifiers plus one main key. Duplicate shortcuts are rejected explicitly after normalization.

## Realm switching automation pause

`enabled=false` means DeskRealm realm switching automation is paused.

While paused:

- the watch loop does not change the Desktop Known Folder;
- DeskRealm desktop hotkeys are ignored;
- manual refresh/switch actions refuse to change realms and show an explicit paused message;
- assignments, realm folders, layouts, files and icons remain untouched.

## Icon layout persistence

| Field | Purpose |
|---|---|
| `iconLayoutPersistenceEnabled` | Enables save/restore of Desktop icon positions. |
| `iconLayoutSettleDelayMs` | Delay before layout capture/restore actions. |
| `iconLayoutAutoSaveEnabled` | Background autosave toggle. Default remains `false`. |
| `iconLayoutAutoSaveIntervalMs` | Interval used when autosave is enabled. |
| `iconLayoutWorkerTimeoutMs` | Timeout for the isolated icon-layout worker. |
| `iconLayoutDisplayTopologyGuardEnabled` | Prevents saves while monitor/resolution/DPI topology is changing. |
| `iconLayoutDisplayTopologySettleDelayMs` | Delay used while display topology settles. |
| `iconLayoutSwitchRestoreDelayMs` | Delay after realm switch before restore. |
| `iconLayoutRestoreRetryCount` | Number of restore retries. |
| `iconLayoutRestoreRetryDelayMs` | Delay between restore retries. |

Icon layout files are stored at:

```text
%APPDATA%\DeskRealm\icon-layouts\<virtual-desktop-guid>.json
```

Each layout file can contain multiple `variants`, one per display topology. The **Icon Layout** UI reads those variants and shows them as child rows. Variant rows display per-monitor working areas from persisted `DisplayX.workingWidth` / `DisplayX.workingHeight` metadata and mark the primary display with `✅`.

## Locks

### `lockedIconLayouts`

Desktop-wide layout locks keyed by virtual desktop GUID:

```json
"lockedIconLayouts": {
  "{00000000-0000-0000-0000-000000000000}": true
}
```

### `lockedRealms`

Realm-wide locks keyed by normalized realm path:

```json
"lockedRealms": {
  "C:\\USERS\\YOU\\DESKTOP\\DESKREALM\\PERSONAL": true
}
```

A realm lock protects every layout variant assigned to that realm. Child rows are disabled in the UI while the parent realm is locked.

### `lockedIconLayoutVariants`

Variant locks keyed by virtual desktop GUID plus display-topology key:

```json
"lockedIconLayoutVariants": {
  "{00000000-0000-0000-0000-000000000000}|display-topology-key": true
}
```

When the current layout/realm/variant is locked, automatic saves use merge-only-new-icons behavior. Existing protected positions are not overwritten; newly detected icons can be appended once.

A full overwrite of a locked layout is possible only after explicit confirmation from the UI or tray manual save path.

## Variant deletion

The **Icon Layout** tab can delete a saved display-topology variant after confirmation. This removes only the selected variant entry from `%APPDATA%\DeskRealm\icon-layouts\<desktop-guid>.json`; it does not delete Desktop files or icons.

If the deleted variant is present in `lockedIconLayoutVariants`, the lock entry is removed at the same time. If the last variant is deleted, the now-empty layout JSON file is removed. Config schema remains `10`.

## Example

```json
{
  "version": 10,
  "enabled": true,
  "pollIntervalMs": 750,
  "restoreDesktopOnExit": true,
  "rejectOneDriveDesktop": true,
  "syncRealmNamesWithVirtualDesktopNames": true,
  "initialDesktopImportPromptEnabled": true,
  "initialDesktopImportPromptCompleted": false,
  "initialDesktopImportMoveFiles": false,
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
    "1": "Win+Shift+X",
    "2": "Win+Shift+C",
    "3": "Win+Shift+B",
    "4": "Win+Shift+N"
  },
  "lockedIconLayouts": {},
  "lockedRealms": {},
  "lockedIconLayoutVariants": {},
  "hotkeyInitialDelayMs": 180,
  "hotkeySwitchStepDelayMs": 160,
  "hotkeySwitchSettleTimeoutMs": 3000,
  "startWithWindows": false,
  "originalDesktopPath": "C:\\Users\\<you>\\Desktop",
  "realmsRoot": "C:\\Users\\<you>\\Desktop\\DeskRealm",
  "nextRealmNumber": 1,
  "assignments": {
    "{00000000-0000-0000-0000-000000000000}": "Personal"
  }
}
```
