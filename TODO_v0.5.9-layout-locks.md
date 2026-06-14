# TODO v0.5.9 — Layout and Realm Locks

## Audit
- [x] Review current v0.5.9 first-run/tray UI flow.
- [x] Review icon layout save/restore path and worker operations.
- [x] Confirm auto-save path uses `SaveIfChanged` during switches and restore-original flows.
- [x] Confirm manual save path is exposed from tray/UI through `SaveIconLayoutNow`.
- [x] Identify config migration point for lock state.

## Implementation block
- [x] Add config v8/v9/v10 fields for locked icon layouts, realm-path scoped locked realms and variant locks.
- [x] Add strict validation/migration for lock dictionaries.
- [x] Add worker operation for locked merge saves that only records new icons.
- [x] Protect automatic layout saves when a layout or realm is locked.
- [x] Allow manual overwrite of locked layouts only through explicit confirmation in the UI.
- [x] Add current layout lock/unlock controls in the main UI.
- [x] Add current realm lock/unlock controls in the main UI.
- [x] Reflect lock state in the status tab.
- [x] Keep tray actions available and route locked manual save through the UI confirmation when needed.

## Validation
- [x] Static audit of changed C# call paths.
- [x] Static audit of JSON config compatibility.
- [x] Update README / CHANGELOG / release notes / patch notes.
- [x] Update CONFIGURATION / INSTALLATION / ARCHITECTURE / SAFETY if impacted.
- [x] Update TODO / TECHNICAL_AUDIT / SMOKE_TEST.
- [x] Package ZIP with `DeskRealm/` root folder.

## Local validation required on Windows
- [ ] Run `dotnet build DeskRealm.sln --configuration Release` or `./scripts/Build-Release.ps1`.
- [ ] Validate UI lock/unlock controls on current realm.
- [ ] Validate auto-save skip/merge behavior after adding a new icon to a locked layout.
- [ ] Validate manual overwrite confirmation on a locked layout.
