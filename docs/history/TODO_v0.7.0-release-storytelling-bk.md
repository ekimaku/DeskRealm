# TODO — v0.7.0 release storytelling and README media (bk)

Status: **closed** — 2026-06-25

## Scope

- Correct the public `v0.7.0` changelog heading so the release helper can match it deterministically.
- Give the public release a human-facing title.
- Rewrite README and release notes so Realm Studio user value leads before implementation detail.
- Restore the README demonstration section with the agreed current realm-switching GIF and add the Realm Studio UI GIF directly beneath it.
- Keep release control, checklist, patch notes and journal aligned.

## Completed

- [x] Changelog header standardized as `v0.7.0 — Realm Studio: Your Windows Workspaces, Made Visible`.
- [x] Release helper now requires a titled `vX.Y.Z` heading instead of a loose text match.
- [x] README opens with user-facing Realm Studio capabilities: direct wallpaper, hotkey, lock, default and startup controls.
- [x] README GIF order fixed: realm switching/layout continuity first, Realm Studio direct controls second.
- [x] Release notes, patch notes, checklist, validation control and development journal aligned.

## Validation

- Markdown links and the two GitHub user-attachment URLs were checked for exact asset IDs.
- PowerShell static syntax reviewed; runtime release validation remains `./scripts/Prepare-GitHubRelease.ps1 -Version 0.7.0` on Windows.
