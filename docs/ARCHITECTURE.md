# DeskRealm architecture

## Product goal

DeskRealm makes Windows virtual desktops feel like separate workspaces by assigning each virtual desktop to its own Desktop folder and icon layout.

## Core flow

```text
Virtual desktop change detected
  -> read current virtual desktop GUID
  -> resolve/create/adopt/rename realm folder
  -> save previous realm icon layout when applicable
  -> redirect Desktop Known Folder to target realm folder
  -> request Explorer/Shell refresh
  -> restore target realm icon layout when applicable
```

## Main services

| Service | Role |
|---|---|
| `VirtualDesktopRegistryService` | Reads virtual desktop GUIDs, names, order, and current desktop from Explorer registry state. |
| `RealmConfigService` | Loads/saves `%APPDATA%\DeskRealm\deskrealm.config.json`. |
| `KnownFolderService` | Gets/sets the current user's Desktop Known Folder path. |
| `ShellRefreshService` | Requests Shell/Explorer refresh after a Desktop path switch. |
| `DesktopSwitchService` | Main orchestrator for switching, folder sync, autosave, restore, and hotkey navigation. |
| `DesktopIconShellService` | Captures/restores visible Desktop icon positions through Shell folder view APIs. |
| `IconLayoutWorkerClientService` | Runs icon layout capture/restore in a worker process to isolate COM/native crashes. |
| `IconLayoutPersistenceService` | Reads/writes icon layout JSON files. |
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
5. then applies the DeskRealm folder/layout switch.

## Icon layout strategy

Icon positions are persisted per virtual desktop GUID:

```text
%APPDATA%\DeskRealm\icon-layouts\<virtual-desktop-guid>.json
```

Each visible icon is keyed by a stable PIDL-derived item key. This avoids relying on display order mismatches between different Shell enumeration APIs.

## Worker isolation

Desktop icon Shell/COM interop is isolated in a worker process. If the Shell view crashes due to native COM behavior, DeskRealm disables icon layout persistence for the current session but keeps Desktop switching alive.

## Runtime state locations

| Data | Location |
|---|---|
| Config | `%APPDATA%\DeskRealm\deskrealm.config.json` |
| Icon layouts | `%APPDATA%\DeskRealm\icon-layouts\*.json` |
| Logs | `%LOCALAPPDATA%\DeskRealm\logs\deskrealm.log` |
| Realm folders | Default: `%USERPROFILE%\Desktop\DeskRealm\...` |
