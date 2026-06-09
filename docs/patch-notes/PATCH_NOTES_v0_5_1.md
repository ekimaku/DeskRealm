# DeskRealm v0.5.1 — Quiet icon layout hotfix

## Goal

Remove the periodic icon layout polling that could cause a tiny busy-cursor flicker every autosave interval.

## Changes

- Config version bumped to `2`.
- `iconLayoutAutoSaveEnabled` default changed from `true` to `false`.
- Existing config migration disables background autosave when upgrading from v1 config.
- `Tick()` no longer launches the icon layout worker when the virtual desktop did not change.
- Active realm layout is saved before desktop switch and before restore-to-original on exit.
- Post-restore Shell refresh removed so Explorer does not redraw/reorder after icons were positioned.

## Expected result

DeskRealm should become invisible during normal idle use: no periodic icon worker, no repeated busy cursor, and icon positions preserved through switch/exit events.
