# DeskRealm v0.3.0 — Patch notes

## Added

- Per-virtual-desktop icon layout persistence.
- Automatic icon layout save before switching away from a realm.
- Automatic icon layout restore after switching into a realm.
- Tray actions:
  - `Save icon layout now`
  - `Restore icon layout now`
- Config keys:
  - `iconLayoutPersistenceEnabled`
  - `iconLayoutSettleDelayMs`
- Status window now displays icon layout persistence state and layout storage path.

## Technical notes

- Icon positions are captured/restored through the supported Shell folder view path:
  - `IFolderView.GetItemPosition`
  - `IFolderView.SelectAndPositionItems`
- Layout files are keyed by virtual desktop GUID, so renaming a Win+Tab space keeps the saved layout attached to that same workspace.
- No fallback layout is used when a layout file or icon is missing.

## Safety

- Duplicate icon display names cause an explicit error because the mapping would be ambiguous.
- First-time realms simply log that no layout exists yet.
- Partial restores log which saved icons were not found.
