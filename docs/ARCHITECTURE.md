# DeskRealm architecture

## Product goal

DeskRealm makes Windows virtual desktops feel like separate workspaces by assigning each virtual desktop to its own Desktop folder and icon layout.

## Core flow

```text
Virtual desktop change detected
  -> read current virtual desktop GUID
  -> resolve/create/adopt/rename realm folder
  -> save previous realm icon layout only when the current desktop/path state is safe
  -> redirect Desktop Known Folder to target realm folder
  -> request Explorer/Shell refresh
  -> defer icon restore until Explorer has settled on the target realm
  -> restore target realm icon layout using the best display-topology variant
```

## Main services

| Service | Role |
|---|---|
| `VirtualDesktopRegistryService` | Reads virtual desktop GUIDs, names, order, and current desktop from Explorer registry state. |
| `RealmConfigService` | Loads/saves `%APPDATA%\DeskRealm\deskrealm.config.json` and migrates config versions. |
| `KnownFolderService` | Gets/sets the current user's Desktop Known Folder path. |
| `ShellRefreshService` | Requests Shell/Explorer refresh after a Desktop path switch. |
| `DesktopSwitchService` | Main orchestrator for switching, folder sync, save guards, restore scheduling, hotkey navigation and restore-on-exit. |
| `DesktopIconShellService` | Captures/restores visible Desktop icon positions through Shell folder view APIs. |
| `IconLayoutWorkerClientService` | Runs icon layout capture/restore in a worker process to isolate COM/native crashes. |
| `IconLayoutPersistenceService` | Reads/writes icon layout JSON files and manages display-topology variants. |
| `DisplayTopologyService` | Captures monitor/resolution/orientation/DPI state used to key layout variants. |
| `GlobalHotkeyService` | Registers system-wide hotkeys and forwards `WM_HOTKEY` events. |
| `KeyboardInputService` | Sends `Win+Ctrl+Left/Right` navigation steps with `SendInput`. |
| `StartupService` | Toggles HKCU Run startup entry. |
| `TrayAppContext` | Tray UI, menu actions, and safe action wrappers. |

## No direct virtual desktop switch API

The public Microsoft `IVirtualDesktopManager` API is window-focused. It can identify or move windows between virtual desktops, but it does not provide a clean public method to jump directly to desktop number N.

DeskRealm therefore:

1. reads the virtual desktop order from Explorer registry state;
2. calculates how many left/right steps are needed;
3. sends official Windows virtual desktop shortcuts;
4. waits until registry state confirms the target desktop;
5. applies the DeskRealm folder/layout switch.

## Icon layout strategy

Icon positions are persisted per virtual desktop GUID:

```text
%APPDATA%\DeskRealm\icon-layouts\<virtual-desktop-guid>.json
```

The layout file can contain multiple variants keyed by display topology:

```text
virtual desktop GUID
  -> exact topology variant: monitors + resolution + orientation + DPI / scale
  -> family topology variant: best-effort compatible monitor family
  -> icons with absolute and screen-relative positions
```

This lets DeskRealm keep valid layouts for normal multi-monitor use, single-monitor fallback, temporary game resolutions and Windows scale changes.

## Icon identity strategy

Icon restore uses layered matching:

1. exact PIDL-derived `itemKey` match;
2. Shell display/parsing/name identity fallback;
3. warning log for unresolved saved icons.

This is required because Explorer may expose the same visible shortcut with a different PIDL after Desktop folder switches, display changes, or Shell reflows. The fallback is especially useful when the same shortcut exists on multiple realms with different positions.

## Save/restore guards

DeskRealm avoids silent contamination with explicit guards:

- skip saves if the known Desktop folder belongs to one realm but the current Windows virtual desktop is another;
- skip saves while display topology is changing/settling;
- defer restore after a switch so previous-realm icons are not mistaken for target-realm icons;
- retry restore if Explorer reflows after the first placement pass.

## Worker isolation

Desktop icon Shell/COM interop is isolated in a worker process. If the Shell view crashes due to native COM behavior, DeskRealm disables icon layout persistence for the current session but keeps Desktop switching alive.

## Runtime state locations

| Data | Location |
|---|---|
| Config | `%APPDATA%\DeskRealm\deskrealm.config.json` |
| Icon layouts | `%APPDATA%\DeskRealm\icon-layouts\*.json` |
| Logs | `%LOCALAPPDATA%\DeskRealm\logs\deskrealm.log` |
| Realm folders | Default: `%USERPROFILE%\Desktop\DeskRealm\...` |
