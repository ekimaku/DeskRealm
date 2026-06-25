# DeskRealm v0.7.0 — Realm Studio Direct Controls & State Propagation (`_be`)

## Scope

Turn Realm Studio cards into direct, truthful control surfaces while keeping all mutable operations serialized through the existing runtime and global-modal architecture.

## Audit and implementation checklist

- [x] Audit the current `_bd` baseline and preserve its validated GUID identity, native Desktop safety, Registry-backed rename, explicit Explorer restart, and tray recovery behavior.
- [x] Define direct-control state ownership: cards are view state only; DesktopSwitchService remains the source of truth; DeskRealmRuntime serializes mutations.
- [x] Define parent/child lock semantics: realm lock is inherited by all variants without destroying pre-existing individual variant locks.
- [x] Add a configuration migration that guarantees exactly one deterministic default realm: native Desktop assignment first, otherwise the first stable Windows desktop.
- [x] Add reverse wallpaper reconciliation from Windows virtual-desktop Registry metadata into DeskRealm-managed wallpaper assets, including safe preview-unavailable reporting.
- [x] Add reusable VCard quick actions for wallpaper draft/save, inline hotkey capture/save/cancel, realm lock/unlock, and default-realm selection.
- [x] Replace VCard total-icon aggregation with the icon count for the current display-topology variant only.
- [x] Replace VCard `Activate` with `Switch`; replace static lock/variant summary cells with direct lock/default controls.
- [x] Add explicit in-app `Restart DeskRealm` recovery using a replacement process that waits for the current single-instance owner to exit.
- [x] Debounce runtime-triggered Studio refreshes and turn refresh exceptions into inline status rather than false blocking snapshot-failed dialogs.
- [x] Update README, CHANGELOG, release notes, patch notes, configuration, installation, architecture, safety, technical audit, smoke test and active TODO.
- [ ] Validate the Windows build/publish and the complete direct-controls smoke matrix on a clean extraction.
