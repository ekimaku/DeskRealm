# TODO — v0.7.0 release readiness and milestone audit (`_bj`)

## Goal

Freeze the validated v0.7.0 Realm Studio milestone as a clean public GitHub release, while documenting the remaining strategic milestones without mixing platform upgrades or new runtime features into the release branch.

## Audit and implementation checklist

- [x] Inspect the current v0.7.0 source tree, release scripts, CI workflow, public documentation, package pins and active TODOs.
- [x] Consolidate v0.7.0 public release notes, README, installation and changelog text; remove local-candidate wording from public-facing documentation.
- [x] Record a release-control document separating user-validated Windows behavior from the final tag/CI actions that must happen at publication time.
- [x] Add a dated roadmap for the next large milestones: testability/architecture, diagnostics/supportability, compatibility/distribution, and later product scope.
- [x] Split GitHub Actions into a read-only build/validation job for main and pull requests, plus a tag-only release job with the only `contents: write` permission.
- [x] Add SHA-256 checksums to CI release assets.
- [x] Archive completed local v0.7.0 candidate TODOs so the repository root contains only current release work.
- [x] Recheck project/version/documentation alignment and package-version decisions.
- [x] Validate XML/JSON/YAML and the final archive structure statically.
- [ ] Run final Windows `Build-Release.ps1`, portable-EXE smoke gate and GitHub Actions tag workflow from the actual Git worktree.

## Intentionally out of scope

- No new Shell behavior, registry mutation path, wallpaper policy or virtual-desktop integration.
- No Windows App SDK/.NET/package upgrade inside the v0.7.0 release freeze.
- No source-repair/preflight machinery is reintroduced.

Release readiness audit completed in local candidate `_bj`; public app version remains `0.7.0`.
