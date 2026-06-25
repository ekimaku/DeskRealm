# DeskRealm architecture

## Runtime model

DeskRealm is an unpackaged WinUI 3 desktop application. It watches Windows virtual-desktop state, maps each virtual-desktop GUID to a realm assignment, switches the Desktop Known Folder to the active realm path, and uses a persistent Shell worker for icon layout persistence.

Core boundaries:

- **VirtualDesktopRegistryService** reads Windows virtual-desktop IDs and names.
- **DesktopSwitchService** owns realm assignment, name changes, native Desktop protection and Desktop Known Folder switching.
- **IconLayoutPersistenceService** captures/restores desktop icon positions through the Shell worker.
- **WallpaperService** reads native Registry assignments, deduplicates/imports managed wallpaper copies and applies GUID-bound wallpaper state only when explicitly requested.
- **ExplorerRestartService** performs an explicit, current-session Explorer restart only after the user chooses immediate Task View name application.
- **NativeTrayIconService** owns the notification-area icon and re-registers it after the Windows `TaskbarCreated` broadcast.
- **GlobalModalHost** provides the reusable modal orchestration layer used by Realm Studio.
- **RestartDeskRealmService** creates an explicit replacement process for Diagnostics recovery, supporting both a published EXE and the `dotnet run` host.

## Realm identity

The Windows virtual-desktop GUID is immutable identity. Realm folder names, labels, wallpaper, hotkeys, default status and protection state are associated with that GUID. The original Windows Desktop is a special assignment: its virtual-desktop label may be changed, but the physical Desktop path is never renamed, moved or remapped.

## Build and release architecture

The build path has one source of truth: the project file and the .NET/Windows App SDK toolchain.

```text
Clean generated output
  → restore project for win-x64
  → build Release
  → publish self-contained single-file win-x64
  → verify DeskRealm.App.exe
```

There is no candidate source restoration, manifest overlay, or source-text preflight in the normal build/run/CI path. A clean extraction into a fresh folder is the supported local-candidate upgrade path.

## Why this is intentionally simple

WinUI/XAML compile errors, Windows App SDK publish requirements and Explorer behavior should be surfaced by their real tooling. Text assertions over implementation shape are brittle and cannot replace compilation or Windows smoke tests.


## Configuration migration ownership — v0.7.0 `_bc`

`RealmConfigService` owns JSON parsing, normalization and file-only migrations through schema v11. It deliberately does not save an older config that still contains number-keyed `desktopHotkeys`: that payload must survive until `DesktopSwitchService` reads the current Windows virtual-desktop list and binds each shortcut to a GUID. `DesktopSwitchService` then owns v12–v16 migration, persists the canonical GUID-bound model and removes the one-time legacy payload.

This removes a former split-brain state where a file-only service could advance the schema past migrations that required live Windows desktop data. Realm path naming and wallpaper application now use one canonical code path rather than optional compatibility toggles.


## Explorer restart metadata recovery — v0.7.0 `_bd`

`VirtualDesktopRegistryService` keeps an in-memory cache of Registry-confirmed virtual-desktop labels keyed by GUID. The cache is used only while Explorer temporarily omits a `Name` value during its own restart; it is not an alternate source of identity. `DesktopSwitchService` separately retires configuration assignments whose GUIDs are absent from current `VirtualDesktopIDs`, preserving their information as archived metadata without moving folders or layouts.

This creates a clear ordering boundary: Windows GUID membership determines whether an assignment is live; the Registry label is presentation metadata; and a transient fallback such as `Desktop 1` never overrides an established GUID-bound realm in the same running process.

## Realm Studio direct-control pipeline — v0.7.0 `_be`

`DeskRealmRuntime.BuildRealmStudioSnapshot` is the single card-state read path. It reconciles each live desktop wallpaper before building a card, resolves the current display-topology variant, and exposes only current-layout icon counts to the dashboard.

The quick card controls call the same serialized runtime operations as the global editor:

```text
Wallpaper draft → SetRealmWallpaperAsync → DesktopSwitchService.SetRealmWallpaper
Inline hotkey → UpdateRealmHotkeyAsync → DesktopSwitchService.UpdateRealmHotkey
Realm lock → ToggleRealmLockAsync → DesktopSwitchService.ToggleRealmLock
Default star → SetDefaultRealmAsync → DesktopSwitchService.SetDefaultRealm
```

No VCard action owns a duplicate state mutation path. The editor remains the detailed view, while cards are lightweight entry points into the same services. Registry/Explorer state changes are debounced before the dashboard refreshes; inline hotkey capture deliberately suppresses card reconstruction until capture ends.

The lock model is additive: `effective = realm lock OR current-layout lock OR variant lock`. A parent lock changes effective state, not the child variant's stored lock preference.


## v0.7.0 `_bf` — Hotkey view-model contract

`RealmCardViewModel` exposes two intentionally separate hotkey values:

- `Hotkey`: raw nullable configuration value consumed by editable controls and modal payloads.
- `HotkeyDisplay`: user-facing card text, which may render `Not assigned`.

Editable flows must use `Hotkey`; presentation-only surfaces must use `HotkeyDisplay`. This prevents a display fallback from crossing into persistent configuration.

## Hotkey capture visual-tree activation — v0.7.0 `_bh`

`HotkeyCaptureField` is the single reusable input/capture primitive. It owns parsing, modifier validation, draft clearing, cancellation and the transient field text (`Waiting input...`, modifier previews, unsupported-key feedback and captured chords). Static help belongs to `ToolTipService`, not permanent VCard layout.

The VCard owns its host-specific actions: **Reset**, **💾** and **×** live together below the full-width field. Reset restores the hotkey that existed when that edit field was opened; it does not persist, remove or register anything. The global runtime owns suspend/register/resume.

Before calling `SuspendGlobalHotkeysForCaptureAsync`, the card host marks the capture active. This is deliberate: suspension emits a runtime state notification, and the resulting refresh must recognize the active capture and preserve the card instance instead of rebuilding it between the first pencil click and the visual-tree `Loaded` callback. Once loaded, `StartCaptureWhenReady()` begins capture and requests `FocusState.Programmatic`.

The unified editor does not auto-arm because its detailed form intentionally keeps its existing click-to-capture interaction. No global hotkey registration occurs until the user commits with the VCard **💾** control or saves the full editor.

## v0.7.0 `_bi` — startup visibility and state-first realm locks

- `RealmConfig.StartMinimized` is a persisted visibility preference owned by the runtime configuration, separate from Windows Run-key registration.
- `DeskRealmLaunchState` carries the resolved preference to `App.OnLaunched`; first-run import explicitly overrides it so the required setup modal cannot be hidden.
- `RealmCardViewModel` exposes realm-lock glyphs as **state**, not command prediction: closed lock means currently locked; open lock means currently unlocked. The action remains explicit in the tooltip and command route.
