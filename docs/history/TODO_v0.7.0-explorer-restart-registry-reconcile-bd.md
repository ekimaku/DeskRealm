# DeskRealm v0.7.0 — Explorer restart registry reconciliation (`_bd` local candidate)

## Context

After a confirmed realm rename followed by the explicit Explorer restart, the registry monitor can reconcile while Explorer is rebuilding virtual-desktop metadata. In that short window, a desktop name can be read as the fallback `Desktop N`. A stale assignment from a deleted virtual desktop can then be mistaken for an active collision and surface as a false **snapshot failed** diagnostic.

## Block To-Do

- [x] Audit the exception source and confirm it is runtime assignment reconciliation, not a stale WinUI modal or tray callback.
- [x] Make virtual-desktop name reads resilient across Explorer restart by retaining the last confirmed name per GUID while the registry name is temporarily unavailable.
- [x] Normalize persisted realm assignment GUID keys at config load so `{GUID}` / `GUID` spellings cannot split one desktop into two mappings.
- [x] Retire stale assignments for virtual desktops no longer present in `VirtualDesktopIDs`, preserving them as archived realm metadata without moving files or layouts.
- [x] Ensure all active-name collision checks ignore non-live assignments as a defensive second layer.
- [x] Add a focused smoke test for rename → Explorer restart → registry reconciliation with no false duplicate/snapshot diagnostic.
- [x] Update README, CHANGELOG, release notes, patch notes, configuration, architecture, safety, technical audit, journal and active TODO.
- [ ] Validate on Windows: build, rename any realm, choose restart Explorer, wait for tray recovery, verify no `snapshot failed` dialog and no `Desktop 1` false conflict.

## Closure

Implementation and documentation are complete. Windows build/publish and the focused Explorer restart smoke check remain the release gate.
