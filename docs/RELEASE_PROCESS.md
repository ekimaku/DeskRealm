# Release process

DeskRealm uses GitHub Actions to build Windows artifacts.

## CI build

Every push to `main` builds the app and uploads workflow artifacts.

A pushed tag matching `v*` creates a GitHub Release and attaches the release assets.

## Public release

Recommended local helper command:

```powershell
.\.local-tools\Publish-DeskRealmRelease.ps1 -Version 0.5.8 -DryRun
.\.local-tools\Publish-DeskRealmRelease.ps1 -Version 0.5.8
```

Manual release flow:

```powershell
git add -A
git commit -m "Release DeskRealm v0.5.8"
git push origin main
git tag -a v0.5.8 -m "DeskRealm v0.5.8"
git push origin v0.5.8
```

The `Build and release` workflow will:

1. restore and build the .NET solution;
2. publish a self-contained Windows x64 app;
3. create a portable ZIP;
4. create an install-bundle ZIP;
5. attach both ZIP files to the GitHub Release for that tag.

## Release artifacts

- `DeskRealm-<version>-win-x64-portable.zip` — simplest user download.
- `DeskRealm-<version>-win-x64-install-bundle.zip` — includes `Install-DeskRealm.ps1` and `Uninstall-DeskRealm.ps1`.

## Release notes source

`CHANGELOG.md` is the source of truth for release notes. The local helper extracts the requested version section and updates the GitHub Release body after the tag-triggered workflow creates the release.

Manual update after the workflow succeeds:

```powershell
gh release edit v0.5.8 --repo ekimaku/DeskRealm --title "DeskRealm v0.5.8" --notes-file ".release-work\release-notes-v0.5.8-from-changelog.md"
```

## Notes

DeskRealm does not currently ship a signed MSI/EXE installer. The install bundle is intentionally transparent PowerShell so users can inspect what it does before running it. A signed MSI may be added later if the project grows.

## Troubleshooting releases

Watch recent runs:

```powershell
gh run list --repo ekimaku/DeskRealm --limit 5
gh run watch --repo ekimaku/DeskRealm --compact --exit-status
```

If a release was created manually or notes need to be refreshed, use `gh release edit --notes-file` rather than recreating the tag.
