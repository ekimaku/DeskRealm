# TODO — v0.7.0 Hotkey Capture Polish (`_bg`, superseded by `_bh`)

## Objective

Polish the reusable `HotkeyCaptureField` used by both the unified Realm Editor and inline Realm VCard editing so capture has a clear single-click entry point, does not compress the input line with the Clear action, and keeps instructional text available without permanently consuming card space.

## Audit

- The Clear button occupied the same horizontal row as the capture field, reducing the visible shortcut width in VCards.
- The long capture instructions were rendered permanently below the field, although they are reference help rather than persistent state.
- An inline VCard pencil click created the field but did not explicitly arm/focus capture once its visual tree was loaded, making the interaction feel like it required a second click.
- The idle capture state did not explicitly say that DeskRealm was listening before the first key press.

## Implementation

- [x] Move **Clear** below the capture field, leaving the shortcut line full width.
- [x] Move the static instruction text into the capture field tooltip.
- [x] Show `Waiting input...` immediately when capture is armed and no key has been pressed.
- [x] Add `StartCaptureWhenReady()` to the reusable control.
- [x] Automatically arm and programmatically focus the inline VCard capture field after it enters the visual tree.
- [x] Preserve existing capture contracts: Escape, modifier release, focus loss and Clear remain explicit and never silently mutate a registered hotkey.
- [x] Keep the unified Realm Editor behavior click-to-capture; only the inline quick-edit flow auto-arms after its explicit pencil action.

## Validation

> Superseded after live Windows validation exposed a refresh-order race and VCard layout clipping. The corrective completion lives in `TODO_v0.7.0-inline-hotkey-layout-bh.md`.

- [x] Static review: the VCard flow routes `✏️` → existing serialized hotkey suspension → `BeginInlineHotkeyEdit()` → `StartCaptureWhenReady()`.
- [x] Static review: `ToolTipService.SetToolTip` is used for the long instructions and no direct Windows hotkey registration path is duplicated.
- [x] C# delimiter / symbol scan completed.
- [ ] Windows build and smoke test required: first VCard pencil click must immediately show `Waiting input...`, focus the capture field and accept a chord.
- [ ] Verify Clear is below the full-width field, tooltip shows instructions, Save / Cancel preserve the existing registration lifecycle, and unified editor capture remains click-to-capture.
