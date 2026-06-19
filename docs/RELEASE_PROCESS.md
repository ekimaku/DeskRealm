# DeskRealm release process

## Development milestone rule

During an unpublished milestone, the application/version metadata stays on the target release number (currently `0.6.0`). Local source archives use alphabetical suffixes only in the ZIP filename to preserve chronological order. Do not create intermediate Git tags or increment the application version for candidate fixes.

Example local archive sequence:

```text
DeskRealm_v0_6_0_aa.zip
DeskRealm_v0_6_0_ab.zip
DeskRealm_v0_6_0_ac.zip
```

The final approved repository state is released once as Git tag `v0.6.0`.

## Pre-release validation

```powershell
.\scripts\Run-DeskRealm.ps1
.\scripts\Build-Release.ps1
```

Complete `SMOKE_TEST.md` and verify `VERSION.txt`, the project version, CHANGELOG and release notes all agree.

## Release helper

Dry run first:

```powershell
.\.local-tools\Publish-DeskRealmRelease.ps1 -Version 0.6.0 -DryRun
```

Publish only after the milestone is approved:

```powershell
.\.local-tools\Publish-DeskRealmRelease.ps1 -Version 0.6.0
```

Equivalent manual flow:

```powershell
git add .
git commit -m "Release DeskRealm v0.6.0"
git push origin main
git tag -a v0.6.0 -m "DeskRealm v0.6.0"
git push origin v0.6.0
```

Expected release assets:

- `DeskRealm-0.6.0-win-x64-portable.zip`
- `DeskRealm-0.6.0-win-x64-install-bundle.zip`

GitHub release notes are sourced from `docs/release-notes/v0.6.0.md` and the release-helper-compatible `## v0.6.0` section in `CHANGELOG.md`.
