# DeskRealm GitHub release checklist — v0.6.0

## Source state

- [x] Windows Debug build/run passes on the current release package.
- [ ] Windows self-contained Release build passes.
- [ ] `SMOKE_TEST.md` is complete; partial local runtime validation is documented in `docs/validation/v0.6.0-release-control.md`.
- [x] Automatic restore works on DeskRealm hotkeys in local testing.
- [ ] Automatic restore works on native virtual desktop changes.
- [ ] Multi-monitor variants, layout locks, realm locks and exact variant locks pass.
- [ ] Manual save on a locked realm changes only the active topology variant; all other variants restore unchanged.
- [ ] `VERSION.txt`, `.csproj`, `CITATION.cff`, README, CHANGELOG and release notes all say `0.6.0`.
- [ ] No local local ZIP suffix appears in compiled/application version metadata.
- [x] Source ZIP contains no `bin`, `obj`, `dist`, `.release-work`, `.git` directory or nested source ZIP.

## Dry run

```powershell
.\.local-tools\Publish-DeskRealmRelease.ps1 -Version 0.6.0 -DryRun
```

- [ ] Review generated notes and commands.
- [ ] Confirm the changelog parser selects `## v0.6.0`.

## Publish one consolidated release

```powershell
.\.local-tools\Publish-DeskRealmRelease.ps1 -Version 0.6.0
```

Expected assets:

- `DeskRealm-0.6.0-win-x64-portable.zip`
- `DeskRealm-0.6.0-win-x64-install-bundle.zip`

- [ ] Download and smoke-test the published portable ZIP on Windows.
- [ ] Verify the embedded executable/tray/window icon.
- [ ] Verify `VERSION.txt` is included in the published payload.
- [x] Mark `v0.6.0` as the new stable public baseline.
