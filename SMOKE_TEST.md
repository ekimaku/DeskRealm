# DeskRealm v0.5.1 — Static smoke test

This package was prepared in a Linux sandbox without the Windows .NET Desktop SDK runtime, so a real `dotnet publish` could not be executed here.

Static checks performed:

- [x] Root ZIP folder is `DeskRealm/`.
- [x] Project version is `0.5.1`.
- [x] Config version is `2`.
- [x] Background icon autosave default is `false`.
- [x] Config migration disables old v1 periodic autosave settings.
- [x] `Tick()` no longer launches periodic icon save when the desktop did not change.
- [x] Layout save still runs before desktop switch.
- [x] Layout save runs before restoring the original Desktop path.
- [x] Post-restore Shell refresh after icon placement was removed.
- [x] `LICENSE` exists and contains Apache-2.0.
- [x] `NOTICE` exists.
- [x] `CITATION.cff` exists.
- [x] `README.md` contains build/run/restore/license sections.
- [x] `CHANGELOG.md` exists.
- [x] `docs/REFERENCES.md` exists.
- [x] `docs/ATTRIBUTION_GUIDE.md` exists.
- [x] `THIRD_PARTY_NOTICES.md` exists.
- [x] `.gitignore` exists.
- [x] `.github` issue and PR templates exist.
- [x] Existing runtime services still present: Known Folder switching, virtual desktop registry reading, icon layout persistence, hotkeys, startup service.

Real validation to run on Windows:

```powershell
.\scripts\Build-Release.ps1
.\dist\DeskRealm\DeskRealm.App.exe
```

Functional checks:

- [ ] switch virtual desktops with `Win + Tab`;
- [ ] confirm realm folder changes;
- [ ] confirm Task View names sync to folders;
- [ ] move icons, switch away, return, confirm layout is saved/restored;
- [ ] quit DeskRealm, relaunch, confirm last active realm layout was saved before restore;
- [ ] confirm no periodic busy-cursor flicker while idle;
- [ ] use direct hotkeys;
- [ ] toggle startup with Windows;
- [ ] test emergency restore script.
