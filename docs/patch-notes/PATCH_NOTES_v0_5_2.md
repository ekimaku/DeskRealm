# PATCH NOTES — v0.5.2

## Goal

Fix icon position contamination between virtual desktops when the same visible desktop item exists on more than one DeskRealm realm but should keep a different position per realm.

## Root cause

DeskRealm v0.5.1 removed background polling and saved icon layouts on desktop switch. That was safe when DeskRealm hotkeys initiated the switch, because the old Windows virtual desktop was still active during capture.

When the user switched desktops externally through Windows, DeskRealm could observe the new virtual desktop while the known Desktop path still pointed to the previous realm. Capturing at that moment could save the current shell view under the previous realm ID, especially visible with identical icon names across realms.

## Fix

- Added a guard before every automatic switch-save.
- DeskRealm now saves icon positions only when the active Windows virtual desktop ID matches the realm assigned to the current known Desktop path.
- Manual save now refuses explicitly if the known Desktop path and current virtual desktop are out of sync.
- Hotkey pre-switch saves remain active and safe.

## Expected behavior

- Same icons can exist on multiple realms with different positions.
- Switching realms no longer overwrites one realm layout with another realm shell view.
- If the user moves icons then switches through Windows UI before DeskRealm can save, DeskRealm will prefer not saving over corrupting another layout.

## Test focus

1. Put the same icon on two realms.
2. Save each realm with different positions once.
3. Switch through DeskRealm hotkeys and verify each realm restores its own position.
4. Switch through Windows UI and verify DeskRealm does not cross-save the wrong layout.
