# DeskRealm TODO

## v0.7.0 public release gate

- [x] Validate Realm Studio in sustained normal Windows use: wallpapers, direct controls, hotkeys, locks/default realm, rename, Explorer restart/reboot, tray recovery and Start minimized.
- [x] Remove candidate-specific source repair and brittle text-preflight machinery.
- [x] Complete release documentation consolidation, public roadmap, checksum assets and least-privilege CI split.
- [x] Keep current platform pins intentionally: .NET SDK 10.0.301, Windows App SDK 2.2.0 and CommunityToolkit.Mvvm 8.4.2.
- [x] Align the public release story: titled `v0.7.0` changelog header, Realm Studio-first README/release notes, and current realm-switching + Realm Studio UI GIFs.
- [x] Repair Windows PowerShell 5.1 parsing of the release-helper titled-header validation without changing the public `v0.7.0 — Release title` format.
- [ ] Run `./scripts/Prepare-GitHubRelease.ps1 -Version 0.7.0` from the final Git worktree.
- [ ] Commit, tag `v0.7.0`, push, and confirm the tag workflow plus downloaded-release smoke pass.

## Next milestone candidates

- [ ] v0.8.0 — Core testability and service boundaries.
- [ ] v0.9.0 — Diagnostics and supportability.
- [ ] v1.0.0 — Distribution, upgrade and compatibility hardening.

See `docs/ROADMAP.md` for scope and exit gates.
