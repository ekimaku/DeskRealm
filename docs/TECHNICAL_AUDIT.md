# DeskRealm — Technical audit v0.6.0

## Scope and verified base

The performance milestone was rebuilt from the verified local source archive. The audit explicitly protects the embedded icon, README demo, first-run onboarding, capture-based hotkeys, layout/realm/variant locks, multi-display variant metadata and variant deletion.


## Release-control status — 2026-06-19

The current package is a documentation/release-control pass over the validated performance build. No runtime code changes are introduced in this pass. Static source/package checks remain required in the Linux sandbox, while Windows-specific Explorer/COM behavior is validated on the user's machine.

Validated locally so far:

- Debug source run through `scripts\Run-DeskRealm.ps1`.
- Startup existing-realm layout recovery.
- Parallel DeskRealm-hotkey destination preparation and smooth switch behavior.
- Targeted Shell refresh and persistent worker startup.

Still required before Git release:

- Self-contained Release build through `scripts\Build-Release.ps1`.
- Full `SMOKE_TEST.md` checklist pass.
- Final published portable/install-bundle asset smoke test.

## Platform audit

| Component | v0.6.0 decision |
|---|---|
| Runtime / SDK | .NET 10, SDK policy `10.0.301` |
| Language | C# 14 |
| UI | Windows Forms retained |
| Shell integration | Known Folder API, `SHChangeNotify`, `IFolderView` retained |
| Virtual desktop discovery | Explorer registry state retained |
| External NuGet packages | None |
| Node/npm/Vite/React/TypeScript/Three/Dexie | Not used; milestone npm audit is not applicable |
| Release output | Self-contained single-file `win-x64` retained |

WinForms and the Shell COM APIs are not the principal latency source. The previous cost came from fixed waits, work executed on the UI thread, repeated worker startup and broad refresh behavior. The milestone modernizes orchestration rather than replacing stable native integration merely for novelty.

## Removed fixed-time behavior

Retired configuration fields include periodic poll intervals, initial hotkey delays, fixed desktop-step delays, restore settle delays and retry counts/delays. Remaining timeout values are upper guardrails. Short adaptive waits yield/back off only while checking concrete state.

## Runtime pipeline

1. A registry notification or DeskRealm hotkey requests reconciliation.
2. Raw notifications are coalesced.
3. One semaphore serializes Shell/state-changing operations from tray, hotkeys, registry reconciliation and the main window outside the UI thread.
4. Direct hotkeys resolve the destination GUID/realm/topology, save the active source layout, and wait for physical modifiers to be released.
5. Confirmed Windows navigation and destination Known Folder/Shell/layout preparation then run concurrently. A final GUID plus layout-result barrier commits the target; a mismatch triggers explicit compensation to the actual active desktop realm.
6. The persistent STA worker enumerates visible PIDLs with `IFolderView::Items` / `IEnumIDList`, avoiding stale count/index traversal while Explorer mutates the view.
7. `E_BOUNDS` / `E_CHANGED_STATE` during collection replacement reset readiness stability and retry within the same bounded timeout.
8. During the same automatic transition-aware path, `E_FAIL` from live `IFolderView` operations is also interpreted as invalidation of a previously enumerated view. This covers direct jumps across multiple virtual desktops; the classification is not enabled for strict manual save/restore.
9. Reboot recovery no longer depends on `RestoreDesktopOnExit` completing. If the process starts while the Known Folder already targets the current realm, the first reconciliation performs an adaptive restore once instead of returning early.
10. The worker waits until Explorer exposes the exact target realm entries (plus allowed Public Desktop / namespace items).
11. Saved coordinates are applied with `IFolderView` and verified; reapplication occurs only while unresolved positions remain.
12. Manual save is routed through `save-current-variant`, not the generic worker command. Mutation is scoped to the exact active topology key; all non-current variants are fingerprinted before mutation and again after serialization/deserialization.
13. The previous `.Take(24)` behavior is removed from variant upsert. At capacity, creation of a new topology fails explicitly rather than evicting the oldest unrelated variant.
14. Failures are explicit. Only the documented transition context retries `E_FAIL`; unrelated failures disable icon persistence for the session rather than silently switching behavior.

## Compatibility and migration

Config schema `11` removes retired timing settings and introduces:

- `shellViewReadyTimeoutMs`
- `iconLayoutRestoreVerificationTimeoutMs`
- `hotkeyModifierReleaseTimeoutMs`
- `desktopStepConfirmationTimeoutMs`

Assignments, hotkeys, startup preference, first-run state, layout locks, realm locks and topology-variant locks remain preserved.

## Known validation boundary

The Linux sandbox cannot execute Windows Explorer, WinForms, registry virtual desktops, COM `IFolderView`, global hotkeys or multi-monitor DPI transitions. Windows Debug/Release build and runtime smoke tests therefore remain mandatory before release.
