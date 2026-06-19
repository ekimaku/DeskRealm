# TODO — v0.6.0 parallel hotkey pipeline

Status: closed — implementation complete; local Windows runtime validation passed; final release checklist pending

## Goal

Use the destination already resolved by a DeskRealm hotkey to overlap Windows virtual-desktop navigation with target-realm loading and icon-layout restoration.

## Invariants

- Save the source realm before changing the Desktop Known Folder.
- Resolve the destination desktop GUID, realm path and layout before navigation starts.
- Start navigation and target-realm preparation together after physical modifiers are released.
- Treat final Windows virtual-desktop GUID as the transaction commit check, not as a prerequisite for preparing the target realm.
- If Windows lands on another desktop, explicitly reconcile the realm and layout of the desktop actually active.
- Do not silently accept navigation mismatch, layout failure or compensation failure.
- Preserve the single serialized outer operation lane, lock behavior, topology variants and all v0.5.9 UX.
- Keep application/release version `0.6.0`; `_af` belongs only to the local ZIP filename.

## Implementation

- [x] Split target-realm application from source-realm save/final commit.
- [x] Run confirmed navigation and target-realm preparation concurrently.
- [x] Add a final GUID barrier and target-layout result barrier.
- [x] Add explicit final reconciliation when speculative target preparation did not complete.
- [x] Add explicit compensation to the actual active desktop on navigation mismatch.
- [x] Add phase-specific `[PERF]` and transaction logs.
- [x] Update architecture, safety, release notes, changelog, TODO and smoke tests.
- [x] Run static validation and package a clean `DeskRealm_v0_6_0_af.zip`.
- [x] Local runtime validation reported smooth behavior on the parallel hotkey pipeline.

## Final validation still useful

- [x] Confirm destination icons begin converging during the Windows desktop animation.
- [ ] Keep one final log sample showing both parallel branches and a matching final GUID barrier.
- [x] Test a direct two-or-more-desktop jump.
- [ ] Exercise mismatch compensation if a reproducible navigation interruption can be produced.
