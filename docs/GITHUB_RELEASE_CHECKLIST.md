# GitHub release checklist

## Repository setup

- [ ] Repository is public: `ekimaku/DeskRealm`.
- [ ] Default branch is `main`.
- [ ] Repository description is set, for example:

  ```text
  Windows utility that gives each virtual desktop its own Desktop folder, icon layout and hotkeys.
  ```

- [ ] Topics are set:

  ```text
  windows virtual-desktops desktop-icons productivity winforms dotnet
  ```

## Before a tag

- [ ] Verify `README.md` renders correctly on GitHub and shows the current version.
- [ ] Verify `CHANGELOG.md` contains the target version section.
- [ ] Verify `docs/release-notes/v<version>.md` exists if the workflow uses it directly.
- [ ] Verify `LICENSE`, `NOTICE`, `CITATION.cff` and `THIRD_PARTY_NOTICES.md` are present.
- [ ] Verify `docs/SAFETY_AND_PRIVACY.md` is linked from the README.
- [ ] Build locally on Windows:

  ```powershell
  .\scripts\Build-Release.ps1
  ```

- [ ] Run DeskRealm manually and confirm:
  - Desktop switching works.
  - Name sync works.
  - Icon layout save/restore works.
  - Same icons on several realms keep separate positions.
  - Multi-monitor / resolution / DPI variants restore correctly.
  - Hotkeys work.
  - Startup toggle works.
  - Restore original Desktop works.

## Create release

Preferred helper:

```powershell
.\.local-tools\Publish-DeskRealmRelease.ps1 -Version 0.5.8 -DryRun
.\.local-tools\Publish-DeskRealmRelease.ps1 -Version 0.5.8
```

Manual fallback:

```powershell
git add -A
git commit -m "Release DeskRealm v0.5.8"
git push origin main
git tag -a v0.5.8 -m "DeskRealm v0.5.8"
git push origin v0.5.8
```

The GitHub Actions workflow will build and attach:

- `DeskRealm-0.5.8-win-x64-portable.zip`
- `DeskRealm-0.5.8-win-x64-install-bundle.zip`

## After release

- [ ] Download both release assets from GitHub.
- [ ] Test the portable ZIP on your machine.
- [ ] Test the install bundle on your machine.
- [ ] Update release notes from `CHANGELOG.md` if the helper did not finish:

  ```powershell
  gh release edit v0.5.8 --repo ekimaku/DeskRealm --title "DeskRealm v0.5.8" --notes-file ".release-work\release-notes-v0.5.8-from-changelog.md"
  ```

- [ ] Add screenshots/GIFs to the README if desired.
- [ ] Create follow-up issues for signed installer, UI settings window and diagnostics panel.
