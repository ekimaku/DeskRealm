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

## Before first tag

- [ ] Verify `README.md` renders correctly on GitHub.
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
  - Hotkeys work.
  - Startup toggle works.
  - Restore original Desktop works.

## Create release

```powershell
git tag v0.5.1
git push origin v0.5.1
```

The GitHub Actions workflow will build and attach:

- `DeskRealm-0.5.1-win-x64-portable.zip`
- `DeskRealm-0.5.1-win-x64-install-bundle.zip`

## After release

- [ ] Download both release assets from GitHub.
- [ ] Test the portable ZIP on your machine.
- [ ] Test the install bundle on your machine.
- [ ] Add screenshots/GIFs to the README if desired.
- [ ] Create follow-up issues for signed installer, UI settings window and optional icon.
