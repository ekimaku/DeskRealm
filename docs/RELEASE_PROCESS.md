# Release process

DeskRealm uses GitHub Actions to build Windows artifacts.

## CI build

Every push to `main` builds the app and uploads workflow artifacts.

## Public release

Create and push a tag:

```powershell
git tag v0.5.1
git push origin v0.5.1
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

## Notes

DeskRealm does not currently ship a signed MSI/EXE installer. The install bundle is intentionally transparent PowerShell so users can inspect what it does before running it. A signed MSI may be added later if the project grows.

## Local release helper

A local helper script can be kept in `.local-tools/Publish-DeskRealmRelease.ps1`. The folder is ignored by Git on purpose, so maintainer automation does not become part of the public repository.

Typical command:

```powershell
.\.local-tools\Publish-DeskRealmRelease.ps1 -Version 0.5.1
```

The helper uses `CHANGELOG.md` as the source for the GitHub Release body and updates the release after the tag-triggered workflow has created the assets.
