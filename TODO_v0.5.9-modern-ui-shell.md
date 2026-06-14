# TODO — v0.5.9 modern UI shell

## Scope

Modernize the v0.5.9 DeskRealm UI after the first Icon Layout tree pass looked too close to a legacy/90s WinForms admin panel.

## Completed

- [x] Audit existing `DeskRealmMainForm` UI structure.
- [x] Remove the classic visible `TabControl` navigation from the main settings window.
- [x] Add custom navigation buttons while keeping all pages available: Overview, Hotkeys, Icon Layout, Actions, Status.
- [x] Add owner-painted rounded panels for the shell header, content holder, realm cards and child layout rows.
- [x] Add owner-painted action buttons and pill labels for cyan/amber visual consistency.
- [x] Restyle the Icon Layout tree to match the darker DeskRealm/Ayahua/Jardin direction.
- [x] Keep all user-facing strings in English.
- [x] Sweep newly user-visible service/status/error strings to English.
- [x] Preserve layout/realm lock behavior; later v0.5.9 variant-lock follow-up bumps config to `10`.
- [x] Update README, CHANGELOG, release notes, patch notes, TODO, smoke test and technical audit.

## Local validation still required

- [ ] Run `./scripts/Build-Release.ps1` on Windows.
- [ ] Open the UI from the tray and verify the new navigation shell.
- [ ] Verify locked realm child rows remain readable and disabled.
- [ ] Verify layout/realm lock/unlock still works after the visual pass.
