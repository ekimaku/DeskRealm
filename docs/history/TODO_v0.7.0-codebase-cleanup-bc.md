# TODO — v0.7.0 codebase cleanup and migration (`_bc` local candidate)

## Goal

Audit the post-`_bb` Realm Studio codebase and remove code paths, configuration switches and repository artifacts that no longer represent the v0.7.0 product model. Preserve the validated runtime behavior: GUID-bound realms, native Desktop protection, explicit Explorer restart after rename, tray recovery, per-realm wallpapers, hotkeys and icon-layout persistence.

## Audit findings before implementation

- [x] The repository simplification removed candidate-template/preflight machinery, but legacy v0.6 compatibility branches remained in runtime code.
- [x] `RealmConfigService` advanced the schema to v15 before `DesktopSwitchService` could execute its Windows-aware v12/v13/v14 migrations. This made the GUID-hotkey migration and adaptive-default migration unreachable for genuinely old config files.
- [x] The current product model always synchronizes managed realm folders with Windows virtual-desktop labels and always enables native per-realm wallpaper behavior. The old `syncRealmNamesWithVirtualDesktopNames`, `nativeRealmWallpapersEnabled` and numbered-fallback realm paths no longer have a supported UI or a coherent current role.
- [x] `desktopHotkeys` remains necessary only as a one-time import payload for v0.6/v0.7-pre-GUID configs. It must not remain active configuration after GUID migration.
- [x] Dead API/UI code is present: `LayoutProtectionModal`, legacy current-desktop lock wrappers, number-based realm switching, a legacy hotkey parser overload, unused runtime façade methods, a startup Registry getter and obsolete WinForms compile exclusions.
- [x] Several snapshot fields are only consumed by the unused protection modal, so they should be removed from the Realm Studio data path.

## Implementation

- [x] Centralize schema sequencing: `RealmConfigService` performs file-only migrations through v11; `DesktopSwitchService` performs Windows-aware v12-v16 migrations in order.
- [x] Introduce config v16: canonical name synchronization/native wallpaper behavior; obsolete compatibility fields are discarded on save.
- [x] Convert `desktopHotkeys` to nullable one-time legacy import metadata, then remove it after GUID migration.
- [x] Remove inactive name-sync fallback, numbered realm fallback and `nextRealmNumber`.
- [x] Remove unused lock modal, stale runtime façade methods and dead current-desktop wrappers.
- [x] Trim unused snapshot fields and their file-snapshot plumbing.
- [x] Remove obsolete WinForms compile exclusions and stale WinForms reference documentation.
- [x] Move completed root TODO history into `docs/history/` to keep the repository root operational.

## Validation

- [x] Review central package versions and defer platform migration to a dedicated copy-first milestone.
- [x] Run static reference audit for removed symbols.
- [x] Parse project/XAML/JSON documents.
- [x] Verify normal scripts contain no repair/preflight/candidate branches.
- [x] Verify ZIP root and absence of generated output.
- [ ] Windows required: `scripts/Build-Release.ps1`, portable launch, realm switch, layout save/restore, rename + Explorer restart + tray recovery, archive reuse / start fresh / ask, and an upgrade-path test from a copy of an old config.
