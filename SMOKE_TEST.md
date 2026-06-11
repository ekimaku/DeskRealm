# DeskRealm v0.5.7 — Smoke test

This package may be prepared outside a full Windows .NET Desktop SDK runtime, so static checks and Windows runtime checks are separated.

## Static package checks

- [x] Root ZIP folder is `DeskRealm/`.
- [x] Project version is `0.5.7`.
- [x] Config version is `5`.
- [x] Layout model version is `3`.
- [x] Background icon autosave default is `false`.
- [x] Display-topology guard settings exist.
- [x] Switch restore delay/retry settings exist.
- [x] Shell identity fallback fields exist: `shellDisplayName`, `shellParsingName`, `identityKeys`.
- [x] First-run Desktop import settings exist.
- [x] `InitialDesktopImportForm` exists.
- [x] `LICENSE` exists and contains Apache-2.0.
- [x] `NOTICE` exists.
- [x] `CITATION.cff` exists and points to version `0.5.7`.
- [x] `README.md` documents v0.5.7 behavior.
- [x] `CHANGELOG.md` includes `## v0.5.7` in a format accepted by the release helper.
- [x] `docs/CONFIGURATION.md` documents config v5 and first-run import.
- [x] `docs/ARCHITECTURE.md` documents topology variants and save guards.
- [x] `.gitignore` exists and excludes `.local-tools/` and `.release-work/`.
- [x] `.github` issue and PR templates exist.

## Build validation on Windows

Run from repository root:

```powershell
.\scripts\Build-Release.ps1
```

Expected output:

```text
.\dist\DeskRealm\DeskRealm.App.exe
```

## Functional checks

- [ ] Launch DeskRealm.
- [ ] Confirm it appears in the tray.
- [ ] On a fresh config, confirm the first-run Desktop import wizard appears before the first automatic realm switch.
- [ ] Test importing the current Desktop into a selected realm without moving files.
- [ ] Test importing with file move only on disposable test shortcuts/files.
- [ ] Confirm import refuses name conflicts instead of overwriting.
- [ ] Switch virtual desktops with `Win + Tab`.
- [ ] Confirm realm folder changes.
- [ ] Confirm Task View names sync to folders.
- [ ] Use direct hotkeys `Win+Shift+W/X/C/V/B/N`.
- [ ] Move icons on each realm, use **Save icon layout now**, switch away and return.
- [ ] Put the same shortcut/icon on multiple realms at different positions and confirm each realm keeps its own position.
- [ ] Change resolution temporarily and confirm DeskRealm does not overwrite the normal layout.
- [ ] Change display scale / DPI temporarily and confirm DeskRealm chooses/restores the matching variant.
- [ ] Test multi-monitor behavior by disabling/sleeping one screen and returning to the normal topology.
- [ ] Confirm no periodic busy-cursor flicker while idle.
- [ ] Toggle startup with Windows.
- [ ] Test **Restore original Desktop**.
- [ ] Test emergency restore script.

## Useful logs to watch

```text
%LOCALAPPDATA%\DeskRealm\logs\deskrealm.log
```

Expected healthy log patterns:

```text
Initial Desktop import completed ...
Icon layout restored ... variant=exact-topology
Icon layout restore identity fallback ...
Icon layout switch-save skipped ... prevent cross-desktop icon position contamination
Display topology changed ... waiting before icon layout save/restore
```
