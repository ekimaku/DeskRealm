# DeskRealm v0.3.3 — Icon identity fix + autosave

## Fix

- Replaced display-name/order-based icon mapping with PIDL-derived `itemKey`.
- Capture now uses only `IFolderView.Item(index)` + `IFolderView.GetItemPosition`.
- Restore maps current visible desktop items by the same PIDL-derived `itemKey`, then calls `IFolderView.SelectAndPositionItems`.
- Old layouts without `itemKey` are rejected explicitly and must be regenerated once.

## New

- Added periodic icon layout autosave.
- Added deterministic layout fingerprint comparison before writing JSON.
- New config keys:
  - `iconLayoutAutoSaveEnabled`: default `true`
  - `iconLayoutAutoSaveIntervalMs`: default `10000`

## Notes

The root folder inside the ZIP remains exactly `DeskRealm/`.
