# DeskRealm release process

## PowerShell source compatibility

All executable `*.ps1` files in DeskRealm are intentionally ASCII-only. Public Markdown documents may use Unicode typography, but PowerShell scripts must construct any required Unicode characters at runtime. This keeps Windows PowerShell 5.1 parsing deterministic when a repository is extracted without a UTF-8 BOM.

## Local release validation

1. Start from a clean extracted repository folder or a clean Git worktree.
2. Run `./scripts/Prepare-GitHubRelease.ps1 -Version X.Y.Z`.
3. Confirm `dist/DeskRealm/DeskRealm.App.exe` exists.
4. Launch the portable EXE and complete the relevant `SMOKE_TEST.md` matrix.
5. Keep `VERSION.txt`, project version, `CHANGELOG.md` and `docs/release-notes/vX.Y.Z.md` aligned.
6. Review `git status` before committing. Generated output, config, logs and release artifacts are never committed.

## CI policy

GitHub Actions uses two security scopes:

```text
pull request / main push
→ read-only Windows restore → build → publish → artifact upload

tag vX.Y.Z
→ same Windows build artifact
→ tag-only release job with contents: write
→ GitHub release + ZIPs + SHA256SUMS.txt
```

The tag must match `VERSION.txt`, and `docs/release-notes/vX.Y.Z.md` must exist. Local suffixes such as `_bi` are never compiled, tagged or exposed as public application versions.

## Public release rule

Publish only after a Windows build, portable launch and focused smoke validation. For DeskRealm, the critical smoke set includes realm switch/layout restore, native Desktop safety, rename → Explorer restart/reboot → Task View, tray recovery, wallpaper sync, direct controls and archive-resolution behavior.
