# DeskRealm v0.7.0 — Hotkey Capture Polish (`_bg`)

## Changed

- The reusable hotkey capture field now uses the full available width. **Clear** moved to the compact row below it.
- Detailed capture help moved to the field tooltip.
- Inline Realm VCard editing calls `StartCaptureWhenReady()` after the explicit **✏️** action. When the field loads, DeskRealm arms the existing capture path, requests focus and displays **Waiting input...** before the first key press.
- The full Realm Editor remains deliberately click-to-capture.

## Safety

- No second global-hotkey registration path was introduced. VCard controls still use the established suspend → capture → validate → save/cancel → re-register lifecycle.
- `Waiting input...` is visual state only and cannot be saved as a shortcut.
- Escape, modifier release, focus loss and Clear keep their explicit existing behavior.

## Validation required

- Run `./scripts/Build-Release.ps1` on Windows.
- Confirm one VCard pencil click enters focused capture, Clear sits below the field, tooltip help appears and Save/Cancel preserve the expected global hotkey behavior.
