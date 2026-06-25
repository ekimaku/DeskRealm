# DeskRealm v0.6.0 — reboot layout recovery block

Base: `DeskRealm_v0_6_0_ad.zip`.

## Audit

- [x] Trace explicit Quit, Windows session ending, Run-key startup and first reconciliation.
- [x] Confirm that `RestoreDesktopOnExit` is only guaranteed on DeskRealm's explicit exit path.
- [x] Identify the startup early return when the Desktop Known Folder already equals the target realm.
- [x] Confirm that this path skipped icon-layout restore entirely in older builds and the pre-fix v0.6.0 package.

## Implementation

- [x] Add a one-shot startup realm restore invariant.
- [x] Keep normal already-on fast-path behavior after the first successful reconciliation.
- [x] Preserve adaptive Shell readiness, exact membership verification and explicit failure behavior.
- [x] Do not add a fixed startup sleep or rely on shutdown cleanup.

## Validation

- [x] Static source and documentation checks.
- [ ] Windows build.
- [ ] Reboot-equivalent runtime smoke test with Known Folder already targeting the active realm.
- [ ] Real Windows reboot smoke test when convenient.
