# DeskRealm configuration

DeskRealm stores configuration under the current user profile. Realm state is GUID-bound so a rename changes presentation and assigned realm path without losing wallpaper, hotkey, default-realm or layout metadata.

## Key settings

- **Automation enabled:** allows startup/default-realm automation.
- **Default realm:** one GUID-bound realm selected for startup behavior.
- **Realm hotkeys:** global shortcuts assigned per realm.
- **Realm and layout protection:** controls rename/deletion and layout mutation behavior.
- **Wallpaper:** managed local image copy associated with a virtual-desktop GUID.
- **Archived realm resolution:** asks, reuses archived layout/wallpaper, or starts fresh only when the requested name belongs to an archived profile and not an active desktop.
- **Rename apply behavior:** Ask, Restart Explorer now, or apply on next reboot. The remembered choice is managed in Automation.

## Build configuration

The app targets `net10.0-windows10.0.19041.0` and is unpackaged (`WindowsPackageType=None`). `EnableMsixTooling` is enabled only for `PublishSingleFile=true` so Windows App SDK can generate required embedded PRI resources for the self-contained single-file publish.

No runtime setting is modified by the development scripts. `Run-DeskRealm.ps1` and `Build-Release.ps1` only clean generated output and invoke .NET tooling.


## Schema v18 and upgrade behavior

Current config uses GUID-keyed `realmHotkeys`, realm profiles, wallpapers, locks and assignments. Managed realm folders always follow their Windows virtual-desktop labels; configured wallpapers are applied as native per-desktop assignments.

For pre-v12 config files, `desktopHotkeys` is retained in memory only until DeskRealm can map its desktop numbers to the current Windows virtual-desktop GUIDs. The migrated config then saves GUID-keyed `realmHotkeys` and omits the legacy field. This mapping is intentionally Windows-aware and runs before later v12–v18 migrations. Copy the config before testing an upgrade if you need rollback evidence.


## Explorer restart reconciliation

The configuration schema is `v18`. During normal load, assignment keys are canonicalized to the brace-form Windows desktop GUID used by DeskRealm. If a Windows virtual desktop was removed outside DeskRealm, its assignment is retired into archived metadata at the next DeskRealm startup; no realm folder, saved icon layout or wallpaper file is moved.

When Explorer is deliberately restarted to apply a Task View name immediately, the application retains the last Registry-confirmed label per GUID while Explorer briefly rebuilds its metadata. A temporary `Desktop N` fallback therefore cannot rename an established realm mapping or be confused with a live rename conflict.

## Direct-control reconciliation

When the Realms dashboard opens or refreshes, DeskRealm reads the Registry wallpaper assignment associated with each live virtual-desktop GUID. If Windows points to a readable image different from DeskRealm's managed image, DeskRealm imports a content-addressed managed copy and refreshes the preview. This is one-way state import: it does not switch desktops, apply a wallpaper, restart Explorer or replace an unreadable Windows path.

A realm lock is parent protection. It makes every child variant effectively protected without deleting individual variant lock choices. When the realm lock is removed, each variant resumes its own stored lock state.

There is exactly one default realm. An existing selected default is preserved; for a configuration with none, DeskRealm selects the native Desktop realm when present, otherwise the first Windows desktop in stable order.

Diagnostics can start a controlled replacement process through **Restart DeskRealm**. It waits for the old single-instance owner to exit, then starts normally; it is not a silent in-process reload.


## v0.7.0 `_bh` — Hotkey presentation and drafts

The empty hotkey configuration state remains `null` / absent in persisted configuration. `Not assigned` is UI-only text and is never written into `realmHotkeys`.

Inline VCard capture keeps an in-memory draft. **Reset** restores the persisted value that was present when editing began; it does not alter `realmHotkeys`. Only **💾** updates the GUID-keyed `realmHotkeys` configuration and triggers global hotkey re-registration.

## Startup visibility

`startMinimized` controls only the Realm Studio window after DeskRealm launches:

- `true` launches directly to the notification area.
- `false` leaves Realm Studio visible after launch.

It does not create or remove the Windows startup registry entry; that remains `startWithWindows`. New configurations default to `true`, and the v18 migration assigns `true` to existing configurations to preserve the prior behavior. The first-run Desktop import safety gate is always shown regardless of this setting.
