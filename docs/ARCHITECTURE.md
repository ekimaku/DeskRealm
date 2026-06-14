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

## First-run flow

On a fresh config, `v0.5.9` delays the first automatic watch-loop switch until onboarding is completed:

```text
startup
-> config created at version 10
-> original Desktop path is still active
-> tray icon is available
-> DeskRealm main UI opens automatically
-> user associates original Desktop OR skips and creates original-Desktop shortcuts
-> polling/switching starts
```

This keeps the original Desktop visible while the user decides what should happen.

## Main services

| Service / UI | Role |
|---|---|
| `VirtualDesktopRegistryService` | Reads virtual desktop GUIDs, names, order and current desktop from Explorer registry state. |
| `RealmConfigService` | Loads/saves `%APPDATA%\DeskRealm\deskrealm.config.json` and migrates config versions. |
| `KnownFolderService` | Gets/sets the current user's Desktop Known Folder path. |
| `ShellRefreshService` | Requests Shell/Explorer refresh after a Desktop path switch. |
| `DesktopSwitchService` | Main orchestrator for switching, folder sync, first-run association/skip, shortcut creation, layout/realm locks, save guards, restore scheduling, hotkey navigation and restore-on-exit. |
| `DesktopIconShellService` | Captures/restores visible Desktop icon positions through Shell folder view APIs. |
| `IconLayoutWorkerClientService` | Runs icon layout capture/restore in a worker process to isolate COM/native crashes. |
| `IconLayoutPersistenceService` | Reads/writes icon layout JSON files, manages display-topology variants and performs locked merge-only-new-icons saves. |
| `DisplayTopologyService` | Captures monitor/resolution/orientation/DPI state used to key layout variants. |
| `GlobalHotkeyService` | Registers system-wide hotkeys and forwards `WM_HOTKEY` events. |
| `KeyboardInputService` | Sends `Win+Ctrl+Left/Right` navigation steps with `SendInput`. |
| `StartupService` | Toggles HKCU Run startup entry. |
| `TrayAppContext` | Tray runtime, safe action wrappers, first-run polling delay and main UI lifecycle. |
| `DeskRealmMainForm` | Main UI: borderless dark shell, onboarding, hotkey capture, Icon Layout variant tree, actions/options and status. |

## UI lifecycle

DeskRealm is tray-first:

```text
normal launch -> tray only
fresh launch  -> tray + first-run UI
tray open     -> shows DeskRealmMainForm
window X      -> hides DeskRealmMainForm
explicit quit -> exits ApplicationContext and restores original Desktop if configured
```

The UI never acts as a silent fallback. If an action fails, it logs the error and shows the error to the user.

## No direct virtual desktop switch API

The public Microsoft `IVirtualDesktopManager` API is window-focused. It can identify or move windows between virtual desktops, but it does not provide a clean public method to jump directly to desktop number N.

DeskRealm therefore:

1. reads the virtual desktop order from Explorer registry state;
2. calculates how many left/right steps are needed;
3. sends Windows virtual desktop shortcuts;
4. waits until registry state confirms the target desktop;
5. applies the DeskRealm folder/layout switch.

## Icon layout strategy

Icon positions are persisted per virtual desktop GUID:

```text
%APPDATA%\DeskRealm\icon-layouts\<virtual-desktop-guid>.json
```

Each layout file can contain multiple `variants` keyed by display topology:

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

This is required because Explorer may expose the same visible shortcut with a different PIDL after Desktop folder switches, display changes or Shell reflows.

## Save/restore guards

DeskRealm avoids silent contamination with explicit guards:

- skip saves if the known Desktop folder belongs to one realm but the current Windows virtual desktop is another;
- skip saves while display topology is changing/settling;
- defer restore after a switch so previous-realm icons are not mistaken for target-realm icons;
- retry restore if Explorer reflows after the first placement pass;
- when a layout variant, desktop-wide layout or realm is locked, automatic saves can only merge newly detected icons and cannot overwrite existing protected positions;
- full manual overwrite of a locked layout requires an explicit confirmation prompt.

## Layout, realm and variant lock model

Config v10 stores three lock scopes:

```text
lockedIconLayouts[virtualDesktopGuid] = true
lockedRealms[normalizedRealmPath] = true
lockedIconLayoutVariants[virtualDesktopGuid|displayTopologyKey] = true
```

The lock decision is made before calling the icon worker:

```text
unlocked auto-save -> worker save-if-changed
locked auto-save   -> worker save-locked-merge-new-icons
manual save locked -> UI/tray confirmation -> worker save
```

`IconLayoutPersistenceService` implements the locked merge behavior by comparing captured icon identity keys against the saved topology variant. Existing saved icons keep their stored positions. Only icons absent from the saved layout are appended to the variant.

## Icon Layout UI tree

The **Icon Layout** UI groups rows by realm path. Child rows are read from each saved icon-layout JSON file's `variants` collection, so alternate monitor/resolution/DPI layouts become visible as separate lockable rows.

Variant rows display each persisted monitor working area separately from `DisplayX.workingWidth` / `DisplayX.workingHeight`, and mark the primary display with `✅`.

Realm locks are inherited parent locks. Child rows remain readable but their lock/delete actions are disabled while the parent realm is locked.

## Variant deletion path

`DeskRealmMainForm` exposes a confirmation-gated `Delete` action on persisted variant rows. The UI calls `DesktopSwitchService.DeleteIconLayoutVariant(...)`, which removes the selected display-topology variant through `IconLayoutPersistenceService.DeleteVariant(...)` and cleans the matching `lockedIconLayoutVariants` key.

If the deleted variant was the last saved variant, the layout JSON file is removed. If variants remain, the newest remaining variant is promoted into the legacy/current top-level layout fields for compatibility with older readers.

## Hotkey capture and pause semantics

The **Hotkeys** UI no longer behaves like free-form text entry. `DeskRealmMainForm` captures `KeyDown` on read-only hotkey fields, reads currently pressed modifier keys, and records the shortcut only when the first non-modifier key is pressed.

Capture completion clears the active capture state so later keys cannot be appended to the shortcut. `KeyUp` cancels capture when all modifier keys are released before any main key, restoring the previous value.

`Config.Enabled` now means realm switching automation. When disabled, `DesktopSwitchService.Tick()`, `SwitchNow()` and `SwitchToDesktopNumber()` refuse to switch realms explicitly. Tray hotkeys are ignored with a visible notification.

## Modern UI shell

DeskRealm uses WinForms with owner-painted controls for the public settings/onboarding UI. Rounded buttons, cards and status pills are rendered through custom paint logic rather than native tab/button chrome, keeping the app lightweight while avoiding the classic disabled-text and bright-title-bar look.

## Branding and icon resources

DeskRealm stores its application icon under `src/DeskRealm.App/Assets/DeskRealm.ico` and references it from `DeskRealm.App.csproj` through `<ApplicationIcon>Assets\DeskRealm.ico</ApplicationIcon>`.

At runtime, `DeskRealmIcon` extracts the embedded icon from the compiled executable and uses it for:

- the Windows notification-area `NotifyIcon`;
- the main DeskRealm window icon;
- the compiled `.exe` icon shown by Windows.

If extraction fails, the app logs a warning before using the default Windows application icon, so branding failure is diagnosable instead of silent.

## Worker isolation

Desktop icon Shell/COM interop is isolated in a worker process. If the Shell view crashes due to native COM behavior, DeskRealm disables icon layout persistence for the current session but keeps Desktop switching alive.

## Runtime state locations

| Data | Location |
|---|---|
| Config | `%APPDATA%\DeskRealm\deskrealm.config.json` |
| Icon layouts | `%APPDATA%\DeskRealm\icon-layouts\*.json` |
| Logs | `%LOCALAPPDATA%\DeskRealm\logs\deskrealm.log` |
| Realm folders | Default: `%USERPROFILE%\Desktop\DeskRealm\...` |
