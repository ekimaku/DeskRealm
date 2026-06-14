# TODO — v0.5.9 hotkey capture stop/cancel

## Scope

Polish the Hotkeys tab capture behavior so shortcuts are captured exactly once and modifier-only attempts cancel cleanly.

## Tasks

- [x] Audit current capture behavior in `DeskRealmMainForm`.
- [x] Prevent `KeyDown` from implicitly restarting capture after a shortcut was already recorded.
- [x] Track the active capture textbox and original value.
- [x] Stop capture immediately on the first non-modifier main key.
- [x] Cancel capture when all modifier keys are released before a main key.
- [x] Restore the previous value on cancellation.
- [x] Keep Back/Delete as explicit clear behavior.
- [x] Update README, CHANGELOG, release notes, patch notes, configuration, architecture, safety, smoke test and technical audit.
- [ ] Run `./scripts/Build-Release.ps1` on Windows with the .NET SDK.

## Expected behavior

- `Win`, then `Ctrl`, then `G` while holding all three records `Win+Ctrl+G` and stops capture.
- `Win`, then `Ctrl`, then release both records nothing and restores the previous value.
- `Win`, then `G`, then `Ctrl` records `Win+G`; the later `Ctrl` is ignored because capture already stopped.
