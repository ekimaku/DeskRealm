# Patch Notes — v0.7.0 Inline Hotkey Layout & First-Click Repair (`_bh`)

## Fixed

- Fixed the first VCard **✏️** click being lost to a dashboard refresh raised while global hotkeys were suspended.
- Fixed clipping and wrapped capture text in the VCard hotkey tile.

## Changed

- The reusable capture control is now field-only. VCard actions render below the input as **Reset**, **💾** and **×**.
- **Reset** restores the shortcut assigned when editing began; it does not clear, save or re-register a hotkey.
- Long instructions remain available through the field tooltip; capture feedback remains inside the field.

## Validation

Run `./scripts/Build-Release.ps1`, then complete the `_bh` section in `SMOKE_TEST.md`.
