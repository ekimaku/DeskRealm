# PATCH NOTES — v0.5.4

## Goal

Fix remaining icon drift when identical shortcuts exist on several DeskRealm realms and the user switches desktops quickly.

## Root cause addressed

Explorer can show the previous realm's icons for a short moment after the known Desktop folder is changed. Restoring or saving icon positions during that transient phase can contaminate the target realm layout.

## Implementation

- Switches now schedule an icon restore instead of running it immediately.
- The scheduled restore only runs once:
  - the current Windows virtual desktop matches the target desktop;
  - the known Desktop path matches the target realm path;
  - the switch restore delay has elapsed.
- Saves are skipped while a restore is pending.
- Restores are retried a small number of times to counter Explorer reflow.
- `IFolderView.SelectAndPositionItems` now receives all matching icons in a single batch.

## Notes

This does not add a silent fallback. If icon persistence fails, DeskRealm still logs the exact error and disables icon layout persistence for the current session only.
