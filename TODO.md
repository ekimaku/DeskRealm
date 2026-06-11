# DeskRealm TODO

## Current stable line: v0.5.7

- [x] Disable periodic icon polling to avoid busy-cursor flicker.
- [x] Guard saves against cross-desktop contamination.
- [x] Store icon layouts per display topology.
- [x] Include monitor/resolution/orientation/DPI / scale in layout context.
- [x] Defer icon restore after fast virtual desktop switches.
- [x] Retry icon restore after Explorer reflows.
- [x] Verify icon positions after Shell placement.
- [x] Add Shell identity fallback for repeated icons/shortcuts across realms.
- [x] Add first-run Desktop import wizard.
- [x] Add config v5 migration so existing installs are not interrupted by the wizard.
- [x] Update README/docs for v0.5.7 behavior.

## Suggested next versions

### v0.6.0 — User-facing settings / diagnostics

- [ ] Settings window for safe config changes.
- [ ] Diagnostic panel showing current realm, known Desktop path, virtual desktop GUID and display topology key.
- [ ] Icon layout diagnostics: unresolved icon list with display name, parsing name and identity keys.
- [ ] Button to reset only one realm layout or one topology variant.

### v0.7.0 — Installer / polish

- [ ] Signed installer research.
- [ ] Better tray icon / branding.
- [ ] Optional README screenshots/GIFs.

## Backlog

- Optional export/import of DeskRealm config and layouts.
- Optional per-realm icon layout cleanup.
- Optional advanced hotkey editor.
- Optional issue template auto-collecting DeskRealm log snippets.
