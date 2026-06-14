# TODO v0.5.9 — Hotkey capture and pause semantics

## Audit

User testing showed two UX problems after the v0.5.9 UI polish:

1. The **DeskRealm enabled** checkbox was ambiguous. In practice it paused the polling loop, but direct DeskRealm hotkeys could still trigger virtual-desktop navigation and realm switching. That made the option feel useless/non-functional.
2. The hotkey editor still behaved like plain text entry. The expected UX is to click a field, press modifier keys, then press one main key. Capture should stop and record the shortcut when the main key is pressed.

## Implementation checklist

- [x] Rename the ambiguous checkbox to **Enable realm switching automation**.
- [x] Add UI copy explaining that disabling automation pauses realm switching and ignores DeskRealm desktop hotkeys without deleting data.
- [x] Guard `DesktopSwitchService.SwitchNow()` while automation is disabled.
- [x] Guard `DesktopSwitchService.SwitchToDesktopNumber()` while automation is disabled.
- [x] Make tray hotkeys show a visible paused notification and skip switching while automation is disabled.
- [x] Convert hotkey text boxes into read-only capture fields.
- [x] Capture modifier-first shortcuts with `KeyDown`.
- [x] Accept `Win`, `Ctrl`, `Alt`, `Shift` as modifier keys.
- [x] Record the shortcut when the first non-modifier main key is pressed.
- [x] Normalize hotkey text through `HotkeyParser` before saving.
- [x] Reject duplicates after normalization.
- [x] Update README, CHANGELOG, release notes, patch notes, configuration, architecture, safety, installation, technical audit and smoke test docs.
- [x] Package a clean ZIP with root folder `DeskRealm/`.

## Validation status

Static validation completed in this environment. Windows build/runtime validation still needs to be run locally with:

```powershell
.\scripts\Build-Release.ps1
```

Manual checks to run on Windows:

- Capture `Win+Ctrl+G` in a hotkey field.
- Save + reload and confirm explicit registration result.
- Try duplicate capture and confirm explicit rejection.
- Disable **Enable realm switching automation**.
- Press a configured DeskRealm hotkey and confirm no realm switch happens while paused.
- Re-enable automation and confirm hotkeys work again.
