# TODO — v0.7.0 Inline Hotkey Layout & First-Click Repair (`_bh`)

## Objective

Complete the VCard shortcut editor after real Windows validation exposed that the first pencil click was being invalidated by a runtime refresh and that the editor layout still compressed the capture field.

## Audit

- Suspending global hotkeys raises a runtime state notification.
- The earlier VCard path recorded the active capture **after** the suspension call, so `RunSafeAsync` refreshed and rebuilt the cards between the first pencil click and the VCard visual update.
- The capture control still owned a status/action row. In the narrow VCard hotkey tile this competed with the parent Save/Cancel controls and caused clipping.
- The old **Clear** action did not match the desired inline draft behavior: the VCard needs a visible **Reset** action that restores the saved shortcut before the edit began.

## Implementation

- [x] Mark inline capture active before the runtime suspend call so its `finally` refresh intentionally preserves the live VCard instance.
- [x] Reduce `HotkeyCaptureField` to the reusable input/capture primitive. Save, Cancel and Reset belong to the hosting VCard action row.
- [x] Remove persistent capture-status text from the shared control. `Waiting input...`, modifier previews and captured chords render directly in the field; long help stays in the tooltip.
- [x] Give the inline editor a full-width field and a separate lower action row: **Reset**, **💾**, **×**.
- [x] Make **Reset** restore the value that existed when the inline editor opened; it never saves, removes or re-registers a hotkey.
- [x] Preserve unified modal capture behavior and direct `Backspace` / `Delete` draft clearing in the reusable field.

## Validation

- [x] Static review: active inline capture is set before `SuspendGlobalHotkeysForCaptureAsync` and clears only when suspend fails, Save completes, or Cancel completes.
- [x] Static review: VCard editing layout uses a full-width content host and keeps quick actions on a separate row.
- [x] XML/XAML parse, JSON parse and C# delimiter/symbol scans completed.
- [ ] Windows build and smoke test required: first pencil click must immediately replace the display with the focused `Waiting input...` field.
- [ ] Verify `Reset` restores the original shortcut; **💾** commits only the selected draft; **×** keeps the persisted shortcut unchanged.
