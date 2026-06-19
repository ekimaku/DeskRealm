# DeskRealm smoke test — v0.6.0 performance milestone

## Static/package checks

- [x] Project, UI, `VERSION.txt` and release documentation use `0.6.0`.
- [x] Source archive is rebuilt from the verified local baseline and published as `v0.6.0`.
- [x] Embedded icon, README demo, hotkey capture, locks, variant deletion and display metadata are preserved.
- [x] Config schema is `11`; retired fixed timing fields are absent from active code.
- [x] No periodic WinForms virtual desktop timer remains.
- [x] Persistent worker protocol is strict BOM-free UTF-8 JSON Lines.
- [x] Explorer readiness requires exact target realm membership.
- [x] Shell view enumeration uses `IFolderView::Items` / `IEnumIDList`; stale count/index traversal is absent.
- [x] Shell refresh is targeted; no `HWND_BROADCAST` / `WM_SETTINGCHANGE` path remains.
- [x] WFO1000 is fixed with explicit property metadata, not suppressed.
- [x] Source ZIP suffix is archive-only; compiled version remains `0.6.0`.
- [x] Manual save uses the dedicated `save-current-variant` worker operation.
- [x] Non-current variant fingerprints are checked after JSON serialization before any layout file write.
- [x] Variant upsert no longer uses silent `.Take(24)` eviction.

## Windows build checks

- [x] `scripts\Run-DeskRealm.ps1` builds and launches Debug on the current release package during local validation.
- [ ] `scripts\Build-Release.ps1` produces the self-contained executable.
- [ ] No compiler warning is promoted to an error.

## Runtime switching

- [x] Startup reconciliation selects the current realm and restores its layout when the Known Folder already targets the active realm.
- [x] Reboot-equivalent test: relaunch while the Known Folder still targets a realm and verify the first reconciliation restores the saved layout instead of logging only `Already on`.
- [ ] A transient `E_BOUNDS` / `E_CHANGED_STATE` is retried within readiness and does not disable icon persistence.
- [ ] A direct jump across at least two virtual desktops retries transition-scoped `GetItemPosition` `E_FAIL` and restores the destination layout without disabling persistence.
- [x] Direct hotkeys save the outgoing layout, then overlap Windows navigation with destination realm/layout preparation; current release package was locally validated as smooth. Before final Git release, keep one log sample showing `Hotkey parallel transaction starting`, `hotkey parallel barrier reached`, and a successful final target GUID.
- [ ] Force or simulate a navigation mismatch and verify DeskRealm explicitly restores the realm/layout of the desktop actually active before reporting the failure.
- [ ] Native `Win+Ctrl+Left/Right` changes trigger registry reconciliation.
- [x] Notification bursts are coalesced in logs during local validation.
- [ ] A switch never logs Shell ready while old realm-only icons are still visible.
- [ ] Manual Restore remains functional.
- [ ] The main window remains responsive during switch/save/restore.

## Layout and topology

- [ ] Exact topology restore passes on the primary test topology.
- [ ] Layout lock protects existing positions and captures only new icons.
- [ ] Realm lock applies to all layouts in the realm.
- [ ] Exact variant lock and variant deletion remain functional.
- [ ] With at least two saved topology variants, lock the realm, move icons, confirm **Save icon layout now**, and verify only the CURRENT variant timestamp/positions change.
- [ ] Switch through every preserved non-current topology and verify its original positions restore unchanged.
- [ ] At 24 variants, saving a previously unseen topology fails explicitly and does not remove the oldest variant.
- [ ] Multi-monitor metadata and primary-display marker remain visible.
- [ ] Display topology changes restore before future saves are accepted.

## Failure behavior

- [ ] Invalid config values fail explicitly.
- [ ] Worker failure disables icon persistence for the session without stopping realm switching.
- [ ] Emergency restore script returns the original Desktop path.

## Release-control note

The current release package has passed static/package validation and local Debug/runtime checks for startup recovery, locked-variant save integrity and the parallel hotkey pipeline. Before attaching compiled binaries, run the self-contained Release build, complete the final smoke checklist, exercise mismatch compensation when reproducible, and verify the published assets.
