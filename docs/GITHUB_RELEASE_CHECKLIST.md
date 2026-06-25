# GitHub release checklist

## Freeze and local validation

- [ ] Start from the validated v0.7.0 worktree; do not overlay a candidate ZIP onto it.
- [ ] Run `./scripts/Prepare-GitHubRelease.ps1 -Version 0.7.0`.
- [ ] Launch `dist/DeskRealm/DeskRealm.App.exe` and perform the final focused smoke pass from `SMOKE_TEST.md`.
- [ ] Verify `VERSION.txt` and project version say `0.7.0`, and the public CHANGELOG/release-notes headings are titled `v0.7.0`.
- [ ] Review README media: the realm-switching GIF uses asset `80818a95-7ed6-40e8-915c-afb4475325f5` and the Realm Studio UI GIF below it uses asset `33d0f1e5-d6c0-4177-9074-ee8a20178d12`.
- [ ] Review `git status`; no generated output, local config, logs or release artifacts may be staged.

## Commit and tag

```powershell
git add -A
git commit -m "release: DeskRealm v0.7.0 Realm Studio"
git tag -a v0.7.0 -m "DeskRealm v0.7.0"
git push origin main
git push origin v0.7.0
```

## GitHub Actions and release assets

- [ ] Confirm the Windows build job passed for the `v0.7.0` tag.
- [ ] Confirm the GitHub release was created from `docs/release-notes/v0.7.0.md`.
- [ ] Confirm release assets include:
  - `DeskRealm-0.7.0-win-x64-portable.zip`
  - `DeskRealm-0.7.0-win-x64-install-bundle.zip`
  - `SHA256SUMS.txt`
- [ ] Download the portable ZIP from the GitHub release, extract it into a fresh folder, launch `DeskRealm.App.exe`, then close through the tray.
- [ ] Mark v0.7.0 as the latest local rollback ZIP after the public asset smoke pass succeeds.
