# DeskRealm v0.7.0 — codebase cleanup (`_bc` local candidate)

## Scope

This local candidate is a maintenance cleanup, not a public version bump. The public application version remains `0.7.0` development and the public stable release remains v0.6.0.

## Removed

- Retired optional realm-name sync, native wallpaper and numbered fallback configuration paths.
- Dead Realm Studio wrappers, the unused layout-protection modal, obsolete current-desktop lock helpers, number-based switching and legacy hotkey parser overload.
- Unused layout snapshot display metadata and stale WinForms compile exclusions.
- Completed TODO files from the repository root; they are retained under `docs/history/`.

## Migrated

- Config schema v16 restores the intended migration order. Legacy number-keyed `desktopHotkeys` are read only long enough to bind them to Windows virtual-desktop GUIDs, then omitted from the saved config.

## Windows validation pending

Run `scripts/Build-Release.ps1`, then complete the focused upgrade and Realm Studio smoke checks in `SMOKE_TEST.md`.
