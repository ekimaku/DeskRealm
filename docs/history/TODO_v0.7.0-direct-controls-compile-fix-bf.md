# DeskRealm v0.7.0 — Direct Controls Compile Repair (`_bf`)

## Scope

Repair the WinUI direct-controls compile regression reported by the Windows Release build without adding a new fallback, preflight system, or behavior change.

## Checklist

- [x] Audit `RealmEditorModal` and `RealmCardViewModel` against the exact CS1061 compiler diagnostic.
- [x] Restore the raw nullable `Hotkey` view-model property required by `HotkeyCaptureField`.
- [x] Keep `HotkeyDisplay` as a display-only property so `Not assigned` is never captured or persisted as a shortcut.
- [x] Scan direct `RealmCardViewModel` member access in the direct-controls surfaces for the same class of mismatch.
- [x] Update README, CHANGELOG, active TODO, release notes, patch notes, smoke test, architecture, configuration, installation, safety, technical audit and project journal.
- [x] Package a clean candidate ZIP with one `DeskRealm/` root and no generated output.
- [ ] Validate standard Windows Release build and direct-controls smoke flow on a clean extraction.
