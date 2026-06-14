# TODO v0.5.9 — UI polish and variant locks

## Scope

Polish the modern DeskRealm UI after Windows visual testing and align the **Icon Layout** tree with the real persisted layout model.

## Tasks

- [x] Audit the modern WinForms shell artifacts reported from Windows runtime screenshots.
- [x] Remove the bright native Windows caption/title bar and replace it with dark custom app chrome.
- [x] Keep close behavior tray-safe: custom close hides to tray, explicit **Quit DeskRealm** remains the real exit.
- [x] Add borderless resize hit-testing so the window remains resizable.
- [x] Fix owner-painted rounded controls so they clear parent backgrounds before drawing.
- [x] Increase Icon Layout card/row heights and spacing to avoid clipped child row borders and clipped header text.
- [x] Read saved icon-layout JSON `variants` and render them as child layout rows in **Icon Layout**.
- [x] Add config v10 with `lockedIconLayoutVariants`.
- [x] Add lock/unlock actions for individual display-topology layout variants.
- [x] Preserve inherited realm lock behavior: child rows disabled/readable while parent realm is locked.
- [x] Update README, CHANGELOG, release notes, patch notes, CONFIGURATION, INSTALLATION/UX notes, ARCHITECTURE, SAFETY, TECHNICAL_AUDIT and SMOKE_TEST.
- [x] Prepare clean ZIP with `DeskRealm/` root.

## Build validation

To run on Windows:

```powershell
.\scripts\Build-Release.ps1
```

Expected: release build succeeds without warnings.
