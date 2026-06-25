# DeskRealm v0.7.0 — Explorer restart reconciliation (`_bd` local candidate)

## Fixed

- Fixed a false **snapshot failed** error that could appear after a realm rename followed by **Apply and restart Explorer**.
- DeskRealm now keeps the last Registry-confirmed desktop label per Windows GUID while Explorer rebuilds temporary metadata.
- Stale configuration mappings from virtual desktops deleted outside DeskRealm are retired into archived metadata at startup and no longer count as live name conflicts.
- Assignment GUID keys are normalized during config load.

## Safety

The cleanup changes only DeskRealm configuration. It does not move, delete, rename or merge any realm folder, icon layout data or wallpaper file.

## Validation pending

Run `scripts/Build-Release.ps1`, then perform rename → Explorer restart → tray recovery and confirm no false `Desktop N` conflict appears.
