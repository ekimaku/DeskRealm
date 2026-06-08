# Release process

DeskRealm uses GitHub Actions to build Windows artifacts.

## CI build

Every push to `main` builds the app and uploads workflow artifacts.

## Public release

Create and push a tag:

```powershell
git tag v0.5.0
git push origin v0.5.0
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
