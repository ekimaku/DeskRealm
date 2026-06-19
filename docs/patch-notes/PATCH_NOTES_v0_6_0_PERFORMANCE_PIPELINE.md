# DeskRealm v0.6.0 — performance pipeline implementation notes

This milestone is rebuilt exclusively from `DeskRealm_v0_5_9_release_ready_with_icon_readme_demo_fix.zip`.

## Internal candidate archive rule

The application, documentation and eventual Git tag stay at `0.6.0`. Local source ZIPs use alphabetical suffixes only in the archive filename to preserve chronological order. These suffixes are not semantic versions and are never compiled into DeskRealm.

## Corrections already folded into the milestone

- Release-control documentation pass: README, CHANGELOG, v0.6.0 release notes, smoke tests, release checklist and technical audit now separate user-visible performance gains, validated local runtime behavior and remaining release-blocking checks.
- Startup existing-realm recovery: the first process reconciliation never accepts `KnownFolder == target realm` as proof that icon positions are correct. It performs one adaptive restore before entering the normal no-op fast path, making reboot recovery independent of graceful shutdown cleanup.
- Stable Shell-view enumeration through `IFolderView::Items` / `IEnumIDList` instead of a stale count plus index loop.
- `E_BOUNDS` and `E_CHANGED_STATE` produced while Explorer replaces the Desktop collection are retried as explicit transient state within the existing readiness timeout; they no longer disable layout persistence immediately.
- `IFolderView::GetItemPosition` may also return generic `E_FAIL` while Explorer crosses multiple virtual desktops and invalidates a previously enumerated PIDL. That HRESULT is treated as transient only in the automatic transition-aware readiness/restore path; strict manual operations still fail explicitly.
- Explicit WinForms designer serialization metadata for custom control properties.
- Strict UTF-8 without BOM on both directions of the persistent worker protocol.
- Exact Explorer realm-membership readiness instead of path-retargeting heuristics.
- Targeted Shell directory notification without the one-second global broadcast path.
- Coalescing of raw registry notification bursts.
- Preservation of the existing UI and layout-variant features during the runtime rebase.
- Main-window Shell/state actions routed through the same serialized background lane as tray, hotkey and registry operations.
- Manual save is now a dedicated current-topology operation. It replaces or creates only the active variant, preserves every other variant byte-for-byte at the serialized data level, and refuses implicit eviction at the 24-variant limit.
- Direct hotkeys now use the destination already resolved during preflight: after source save and modifier release, Windows navigation and target Known Folder/Shell/layout restoration run concurrently. A final active-GUID barrier commits the transaction or explicitly compensates to the realm Windows actually reached.
