# DeskRealm TODO

## Current stable line: v0.5.9

### Completed for v0.5.9

- [x] Add DeskRealm UI accessible from the tray.
- [x] Keep normal runtime tray-first and open UI automatically only on fresh first run.
- [x] Hide UI to tray on window close.
- [x] Add explicit **Quit DeskRealm** action in the UI.
- [x] Add first-run explanation inside the UI.
- [x] Delay first automatic switch until first-run decision is completed.
- [x] Keep first-run Desktop association safe: no Desktop file moves.
- [x] Add skip onboarding path creating shortcuts to the original Desktop.
- [x] Add hotkey capture fields with strict validation and immediate reload.
- [x] Replace default hotkeys with `Win+Shift+X/C/B/N` for desktops 1-4.
- [x] Clarify **Enable realm switching automation** semantics.
- [x] Prevent automatic/manual/hotkey realm switching while automation is paused.
- [x] Add dedicated **Icon Layout** tab.
- [x] Render saved display-topology variants as child rows.
- [x] Show separate per-display working resolutions and primary display markers.
- [x] Add layout, realm and variant locks.
- [x] Add locked autosave merge mode for new icons without overwriting existing locked positions.
- [x] Add explicit confirmation before overwriting a locked layout manually.
- [x] Add confirmation-gated variant deletion.
- [x] Replace classic tab strip with a modern DeskRealm navigation shell.
- [x] Restyle the UI with dark custom chrome, rounded cards, owner-painted buttons and status pills.
- [x] Keep public UI text in English.
- [x] Clean release documentation for `v0.5.8` -> `v0.5.9`.
- [x] Add DeskRealm branded executable/tray/main-window icon.
- [x] Keep `CHANGELOG.md` compatible with `.local-tools/Publish-DeskRealmRelease.ps1`.

## Release validation still required locally

- [ ] Run `./scripts/Build-Release.ps1` on Windows.
- [ ] Confirm the build succeeds without warnings.
- [ ] Run the `SMOKE_TEST.md` manual checks that apply to your setup.
- [ ] Run `.\.local-tools\Publish-DeskRealmRelease.ps1 -Version 0.5.9 -DryRun` before publishing.
- [ ] Run `.\.local-tools\Publish-DeskRealmRelease.ps1 -Version 0.5.9` after dry-run validation.

## Suggested next versions

### v0.6.0 — Diagnostics polish

- [ ] Diagnostic panel showing current display topology key and active layout variant.
- [ ] Icon layout diagnostics: unresolved icon list with display name, parsing name and identity keys.
- [ ] Button to reset only one realm layout or one topology variant.
- [ ] Visual list of locked layouts/realms/variants with direct unlock buttons.
- [ ] Button to recreate original-Desktop shortcuts for a selected realm only.

### v0.7.0 — Installer / distribution polish

- [ ] Signed installer research.
- [ ] Optional README screenshots/GIFs.
- [ ] Optional first-run mini GIF / visual explanation.

## Backlog

- Optional export/import of DeskRealm config and layouts.
- Optional per-realm icon layout cleanup.
- Optional issue template auto-collecting DeskRealm log snippets.
