# TODO — v0.7.0 release helper encoding repair (`_bl`)

## Audit

- [x] Reproduce from the Windows PowerShell parser log.
- [x] Identify the raw UTF-8 em dash in `Prepare-GitHubRelease.ps1` as the only non-ASCII executable PowerShell source.
- [x] Confirm the file had no UTF-8 BOM and that the public CHANGELOG header itself was correct.

## Implementation

- [x] Keep the public `CHANGELOG.md` heading format `## v0.7.0 — Release title`.
- [x] Construct the em dash at runtime with `[char]0x2014` in the helper.
- [x] Keep all executable PowerShell scripts ASCII-only.
- [x] Update release documentation, release notes, patch notes and release control.

## Validation

- [x] Static scan confirms no non-ASCII bytes remain in executable `*.ps1` files.
- [x] Static scan confirms the titled v0.7.0 CHANGELOG header remains present.
- [x] ZIP inspected with one `DeskRealm/` root and without generated output.
- [ ] Windows validation: run `./scripts/Prepare-GitHubRelease.ps1 -Version 0.7.0`.
