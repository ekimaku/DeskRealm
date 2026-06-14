# DeskRealm smoke test — v0.5.9

## Static release checks

- [x] Project version is `0.5.9`.
- [x] Config version is `10`.
- [x] Layout model version remains `3`.
- [x] `README.md` announces the v0.5.9 UX and layout-management features.
- [x] `CHANGELOG.md` contains a release-helper-compatible `## v0.5.9` section.
- [x] `docs/release-notes/v0.5.9.md` documents the user-facing release.
- [x] `docs/patch-notes/PATCH_NOTES_v0_5_9.md` documents the technical delta from `v0.5.8`.
- [x] `docs/CONFIGURATION.md` documents config v10.
- [x] `docs/ARCHITECTURE.md` documents the UI lifecycle, first-run delay, locks, variants, hotkey capture and pause semantics.
- [x] `docs/SAFETY_AND_PRIVACY.md` documents first-run, lock, deletion, pause and hotkey-capture safety.
- [x] `TODO.md` is consolidated for release readiness.

## Windows build validation

Run from repository root:

```powershell
.\scripts\Build-Release.ps1
```

Expected output:

```text
.\dist\DeskRealm\DeskRealm.App.exe
```

Expected build status:

```text
Générer a réussi
```

The previous nullable warnings in `DeskRealmMainForm` around `SystemFonts.MessageBoxFont` should not reappear.

## First-run checks

- [ ] Launch DeskRealm with a fresh config.
- [ ] Confirm the DeskRealm UI appears before the first automatic realm switch.
- [ ] Confirm `DeskRealm.App.exe` shows the DeskRealm `DR` icon in File Explorer.
- [ ] Confirm the tray icon is visible and uses the DeskRealm `DR` icon instead of the generic Windows application icon.
- [ ] Test associating the current Desktop to a selected realm without moving files.
- [ ] Reset fresh config, test skipping association and confirm `DeskRealm - Original Desktop.lnk` appears in managed realms.
- [ ] Confirm skip path does not complete silently if shortcut creation fails.

## UI lifecycle checks

- [ ] Open the UI from the tray.
- [ ] Open the UI by double-clicking the tray icon.
- [ ] Close the UI with the cross and confirm DeskRealm remains in the tray.
- [ ] Use **Quit DeskRealm** from the UI and confirm the app exits.
- [ ] Confirm public UI strings are English.
- [ ] Confirm the dark custom title/chrome is visible instead of the native bright title bar.
- [ ] Confirm navigation/action buttons have clean rounded edges with no native button artifacts.

## Hotkey capture checks

- [ ] Open **Hotkeys**.
- [ ] Click desktop #1 field.
- [ ] Press `Win`, then `Ctrl`, then `G` while holding all three.
- [ ] Confirm the field records `Win+Ctrl+G` immediately when `G` is pressed.
- [ ] Click another field, press `Win`, then `Ctrl`, then release both without pressing a main key.
- [ ] Confirm capture cancels and the previous field value is restored.
- [ ] Click another field, press `Win`, then `G`, then press `Ctrl` while still holding keys.
- [ ] Confirm the field records `Win+G` and ignores the later `Ctrl`.
- [ ] Click **Save + reload** and confirm the shortcut is persisted or explicitly rejected by Windows if unavailable.
- [ ] Try entering the same captured shortcut on another desktop and confirm duplicate detection rejects it.

## Pause semantics checks

- [ ] Disable **Enable realm switching automation**.
- [ ] Press a configured DeskRealm desktop hotkey and confirm DeskRealm does not change realms.
- [ ] Click **Refresh now** and confirm DeskRealm shows an explicit paused/refused message instead of switching silently.
- [ ] Re-enable realm switching automation and confirm hotkeys can switch again.

## Icon Layout checks

- [ ] Save at least one icon layout.
- [ ] Open **Icon Layout**.
- [ ] Expand a realm and confirm saved display-topology variants appear as child rows.
- [ ] Confirm variant rows show each display working resolution separately, for example `Display 1: 2304x1040 ✅ · Display 2: 2304x1040`.
- [ ] Confirm `CURRENT` appears on the active variant when applicable.
- [ ] Lock/unlock a child variant and confirm `lockedIconLayoutVariants` updates.
- [ ] Lock a realm and confirm child rows remain readable but lock/delete actions are disabled.
- [ ] Lock current layout, move an existing icon, switch away/return and confirm the original locked position is restored.
- [ ] Add a new icon to a locked layout, switch away/return and confirm the new icon position is captured once.
- [ ] Try **Save icon layout now** on a locked layout and confirm the overwrite confirmation appears.
- [ ] Cancel the locked-layout save prompt and confirm the saved layout is not overwritten.
- [ ] Confirm the locked-layout manual overwrite works after explicit confirmation.

## Variant deletion checks

- [ ] Expand a realm with at least two saved layout variants.
- [ ] Click `Delete` on a non-current stale variant.
- [ ] Cancel the confirmation and verify no row disappears.
- [ ] Click `Delete` again and confirm.
- [ ] Verify only that variant row disappears.
- [ ] Verify Desktop icons/files remain untouched.
- [ ] Lock the parent realm and verify child `Delete` actions are disabled/readable.

## General runtime checks

- [ ] Switch virtual desktops with `Win + Tab`.
- [ ] Confirm realm folder changes.
- [ ] Confirm Task View names sync to folders.
- [ ] Put the same shortcut/icon on multiple realms at different positions and confirm each realm keeps its own position.
- [ ] Change resolution temporarily and confirm DeskRealm does not overwrite the normal layout.
- [ ] Change display scale / DPI temporarily and confirm DeskRealm chooses/restores the matching variant.
- [ ] Test multi-monitor behavior by disabling/sleeping one screen and returning to the normal topology.
- [ ] Confirm no periodic busy-cursor flicker while idle.
- [ ] Toggle startup with Windows from tray and UI.
- [ ] Test **Restore original Desktop**.
- [ ] Test emergency restore script.

## Useful logs to watch

```text
%LOCALAPPDATA%\DeskRealm\logs\deskrealm.log
```

Expected healthy log patterns:

```text
Initial Desktop import completed ...
Initial Desktop import skipped. Original Desktop shortcuts created ...
Original Desktop shortcut created ...
Hotkey registered: Win+Shift+X -> desktop #1
Icon layout restored ... variant=exact-topology
Icon layout locked autosave skipped ... no new icons detected
Icon layout locked autosave merged new icons ...
Locked icon layout manual overwrite confirmed ...
Icon layout restore identity fallback ...
Icon layout switch-save skipped ... prevent cross-desktop icon position contamination
Display topology changed ... waiting before icon layout save/restore
```
