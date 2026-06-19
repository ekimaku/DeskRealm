# DeskRealm v0.6.0 — Manual current-variant save integrity

Base candidate: `DeskRealm_v0_6_0_ac.zip`.

The project/release version remains `0.6.0`. The next local source package is `DeskRealm_v0_6_0_ad.zip`; the `ad` suffix is local ZIP chronology only.

## Problem

On a locked realm/layout, confirmation-gated **Save icon layout now** can make every saved display-topology variant appear overwritten by the current positions. Manual overwrite must be scoped to the exact active display-topology variant only.

## Implementation block

- [x] Trace the UI/tray manual-save path through the persistent worker and layout JSON writer.
- [x] Add a dedicated `save-current-variant` worker operation for manual save.
- [x] Replace only the exact current topology variant, or add it when no exact variant exists.
- [x] Preserve every non-current variant without changing its key, family, topology metadata, timestamp or icon positions.
- [x] Refuse creation of a 25th variant instead of silently evicting another saved variant.
- [x] Add a pre-write/post-mutation integrity assertion for all preserved variants.
- [x] Keep lock confirmation behavior unchanged and explicit.
- [x] Add precise logs naming the replaced topology and preserved variant count.
- [x] Update README, CHANGELOG, v0.6.0 release notes, patch notes, architecture, safety, technical audit and smoke tests.
- [x] Run static/source validation available in the Linux sandbox.
- [x] Package `DeskRealm_v0_6_0_ad.zip` with `DeskRealm/` as the only root folder.

## Windows validation

- [ ] Build with `scripts\\Run-DeskRealm.ps1`.
- [ ] On a realm containing at least two topology variants, lock the realm, confirm manual overwrite, and verify only the CURRENT variant timestamp/positions change.
- [ ] Switch to every non-current topology and verify its stored positions remain unchanged.
- [ ] Verify cancellation leaves all variants unchanged.
