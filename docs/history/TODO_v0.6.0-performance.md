# DeskRealm v0.6.0 — Performance milestone

Base verified: `DeskRealm_v0_5_9_release_ready_with_icon_readme_demo_fix.zip`.

Local candidate ZIPs use alphabetical suffixes only in their filenames. Project/release version remains `0.6.0`.

## Audit and implementation block

- [x] Rebase from the verified local source ZIP.
- [x] Preserve branding, embedded icon, README demo GIF, onboarding, capture-based hotkeys, locks and variant management.
- [x] Audit fixed sleeps, polling, synchronous UI work, worker process lifecycle and Shell refresh behavior.
- [x] Move switch/save/restore work off the WinForms UI thread through one serialized execution lane.
- [x] Replace periodic virtual-desktop polling with registry notifications and explicit error reporting.
- [x] Replace fixed hotkey delays with physical-modifier release detection and per-step desktop GUID confirmation.
- [x] Reuse one persistent STA icon-layout worker with a strict UTF-8-no-BOM JSON-lines protocol.
- [x] Replace fixed restore delays/retries with adaptive Shell-view readiness and position verification.
- [x] Require target realm membership before accepting the Explorer view as ready; reject partially transitioned views.
- [x] Replace fragile `ItemCount` + indexed `Item` traversal with `IFolderView::Items` / `IEnumIDList`, and keep `E_BOUNDS` / `E_CHANGED_STATE` inside the adaptive readiness loop.
- [x] Keep `GetItemPosition` `E_FAIL` inside the adaptive loop only for automatic transition-aware restores, covering multi-desktop jumps without weakening strict manual save/restore failures.
- [x] Keep targeted Shell directory notification; remove global broadcast refresh.
- [x] Synchronize multi-process logging without retry sleeps.
- [x] Migrate the source build to the current supported .NET LTS while preserving self-contained win-x64 release output.
- [x] Resolve WFO1000 property serialization metadata without suppressing the analyzer.
- [x] Keep project/UI/release version at `0.6.0`; use alphabetic ordering only in local source ZIP filenames.
- [x] Update README, CHANGELOG, release notes, patch notes, architecture, configuration, installation, safety, audit, smoke tests and release workflow.
- [x] Route main-window Shell/state actions through the same serialized background lane as tray, hotkey and registry operations.
- [x] Scope manual save to the exact active topology variant and integrity-check every preserved variant through JSON round-trip validation.
- [x] Remove silent oldest-variant eviction at the 24-variant limit.
- [x] Restore the active realm once on first process reconciliation even when the Desktop Known Folder already points to it, so reboot recovery does not depend on graceful exit.
- [x] Run direct-hotkey navigation and pre-resolved destination realm/layout preparation concurrently after source save and modifier release, with final GUID commit and explicit actual-realm compensation on mismatch.
- [x] Run source/static validation available in the Linux sandbox.
- [x] Package a clean ZIP with `DeskRealm/` as the only root folder.

## Windows validation

- [ ] Build Debug through `scripts\\Run-DeskRealm.ps1`.
- [ ] Build Release through `scripts\\Build-Release.ps1`.
- [ ] Validate startup and switch-time layout restore while Explorer is actively replacing a large Desktop view; no `E_BOUNDS` / `E_CHANGED_STATE` may disable persistence.
- [ ] Validate reboot-equivalent startup with the Known Folder already targeting the active realm; the saved layout must restore once before the normal `Already on` fast path is allowed.
- [ ] Validate a direct jump across at least two virtual desktops; transient `GetItemPosition` `E_FAIL` must retry within readiness and must not disable persistence.
- [ ] Validate parallel hotkey target preparation, final GUID commit, and mismatch compensation; validate native Win+Ctrl+Arrow reconciliation separately.
- [ ] Validate correct realm icon membership before restore success is logged.
- [ ] Validate multi-monitor/topology variants and layout/realm locks.
- [ ] Validate locked-realm manual save changes only the current topology variant and leaves every other variant unchanged.
- [ ] Review `[PERF]` timings and notification coalescing in the log.
