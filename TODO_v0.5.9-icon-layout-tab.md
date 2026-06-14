# TODO v0.5.9 — Icon Layout tab and English UI polish

## Audit
- [x] Review v0.5.9 layout/realm lock implementation.
- [x] Identify that lock controls were too current-action oriented.
- [x] Confirm app UI language must remain English.
- [x] Review visual direction from related project screenshots: dark background, cyan borders, amber accents, card/tree layout.

## Implementation block
- [x] Add dedicated `Icon Layout` tab.
- [x] Render realms as collapsible parent rows with lock and expand controls.
- [x] Render child layouts with lock controls and `CURRENT` marker.
- [x] Disable/grayscale child layout rows when parent realm is locked.
- [x] Allow layout lock/unlock from non-current rows while preserving strict behavior.
- [x] Change realm lock semantics to normalized realm-path scope so all child layouts are protected.
- [x] Migrate config schema to v9 for realm-path lock keys, then v10 for variant lock keys.
- [x] Correct newly added UI strings back to English.
- [x] Apply dark cyan/amber visual pass to the settings window.

## Validation
- [x] Static audit of changed C# call paths.
- [x] Static audit of config v9/v10 migrations.
- [x] Update README / CHANGELOG / release notes / patch notes.
- [x] Update CONFIGURATION / INSTALLATION / ARCHITECTURE / SAFETY.
- [x] Update TODO / TECHNICAL_AUDIT / SMOKE_TEST.
- [x] Package ZIP with `DeskRealm/` root folder.

## Local validation required on Windows
- [ ] Run `./scripts/Build-Release.ps1`.
- [ ] Open the main UI and confirm the app text is English.
- [ ] Test realm expand/collapse in the Icon Layout tab.
- [ ] Lock/unlock a realm and confirm child rows disable/enable.
- [ ] Lock/unlock a child layout and confirm config updates.
- [ ] Confirm locked auto-save still merges only new icons.
