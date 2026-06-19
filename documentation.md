# DeskRealm technical project state

Current development milestone: `v0.6.0` adaptive performance pipeline.

Reboot recovery note: the first process reconciliation now restores the active realm once even when Windows already left the Desktop Known Folder on that realm. This prevents the old `Already on` shortcut from bypassing layout restoration after an unclean shutdown.

Stable public baseline: `v0.6.0`.

The milestone was rebuilt from the verified local source archive and is now the public `0.6.0` baseline. Internal candidate ordering is represented only by the local ZIP filename; the project, application and Git release remain `0.6.0`.

## Current block

- Event-driven virtual desktop monitoring.
- Serialized off-UI lane shared by tray, hotkeys, registry reconciliation and main-window Shell/state actions.
- Parallel direct-hotkey transaction: source save first, then confirmed Windows navigation and pre-resolved destination realm/layout preparation together, followed by final GUID commit or explicit actual-realm compensation.
- Persistent strict icon-layout worker.
- Adaptive Explorer readiness and icon-position verification.
- Stable live Shell-view enumeration through `IFolderView::Items` / `IEnumIDList`; `E_BOUNDS` / `E_CHANGED_STATE`, plus transition-scoped `GetItemPosition` `E_FAIL` during automatic multi-desktop jumps, remain bounded transition states instead of disabling persistence.
- .NET 10 / C# 14 source migration.
- Full preservation of the existing UX and safety model while replacing the fragile runtime path.

See `TODO_v0.6.0-performance.md`, `docs/TECHNICAL_AUDIT.md` and `SMOKE_TEST.md`.

## v0.6.0 — manual current-variant save integrity

The confirmation-gated manual save path now uses a dedicated `save-current-variant` worker command. It replaces only the exact active display-topology variant. Every non-current variant is fingerprinted before mutation and after JSON serialization/deserialization; any mismatch aborts before the layout file is written. Creating a new topology at the 24-variant limit fails explicitly instead of evicting the oldest variant.
