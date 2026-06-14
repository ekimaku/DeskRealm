# DeskRealm — Technical audit v0.5.9

## Scope

This audit covers the `v0.5.8` -> `v0.5.9` release delta.

`v0.5.8` stabilized the high-risk first-run model by associating the original Windows Desktop path without moving files. `v0.5.9` keeps that safety model and adds a user-facing UX layer around onboarding, hotkeys, layout management, realm/layout locks and runtime actions.

## Version alignment

| Item | Expected | Status |
|---|---:|---|
| Project version | `0.5.9` | aligned |
| Config version | `10` | aligned |
| Icon layout model version | `3` | unchanged |
| Public UI language | English | aligned |
| Changelog release section | `## v0.5.9` | compatible with release helper |
| Root ZIP folder | `DeskRealm/` | required for source package |

## Branding/icon integration

The v0.5.9 release now includes a proper DeskRealm icon path:

- `Assets/DeskRealm.ico` is referenced by `DeskRealm.App.csproj` as the application icon.
- `DeskRealmIcon` extracts the embedded executable icon for the tray and main window.
- The old generic `SystemIcons.Application` tray icon is no longer used during normal operation.
- Icon extraction failures are logged with `WARN` before the default icon is used.

## Release-helper compatibility

The local release helper extracts release notes from `CHANGELOG.md` by locating the requested `## v0.5.9` section and taking every line until the next `##` release heading.

The `v0.5.9` changelog section is therefore kept as the source of truth and includes all user-facing and technical categories needed for the GitHub Release body:

- Added
- Changed
- Fixed
- Safety
- Documentation

## User-facing additions

- `DeskRealmMainForm` provides a tray-opened UI for onboarding, hotkeys, Icon Layout management, actions/options and status.
- Normal launches remain tray-first; fresh configs open the UI before the first automatic Desktop switch.
- The window close button hides to tray. **Quit DeskRealm** is the explicit exit path.
- The UI includes the main tray actions so non-technical users do not need to discover everything through the context menu.
- The visible shell is modernized with dark custom chrome, owner-painted navigation/buttons/cards/pills and readable disabled states.

## First-run behavior

- First automatic polling/switching is delayed until onboarding is complete.
- Association of the original Desktop remains no-move and uses an absolute path assignment.
- Skip flow creates `DeskRealm - Original Desktop.lnk` shortcuts inside managed realm folders.
- Shortcut creation failures are surfaced explicitly.

## Hotkey behavior

- Defaults changed to `Win+Shift+X/C/B/N` for desktops 1-4.
- Migration preserves existing custom hotkeys and only replaces untouched legacy defaults.
- Hotkey fields are capture-based, not free-form text entry.
- Capture records the shortcut on the first non-modifier key.
- Capture cancels if only modifiers are released.
- Later modifiers are ignored after the main key because capture has already stopped.
- Duplicate/invalid shortcuts are rejected explicitly after normalization.

## Pause semantics

`Config.Enabled` is now documented and presented as **Enable realm switching automation**.

When disabled:

- watch-loop switching is paused;
- direct DeskRealm desktop hotkeys are ignored;
- manual refresh/switch paths refuse to switch realms explicitly;
- existing assignments, realm folders, icons, files and layouts are not changed.

This removes the ambiguous state where disabling DeskRealm appeared to pause only icon positioning while hotkeys could still move realms.

## Icon Layout management

The **Icon Layout** tab now reflects the real persisted model:

```text
Realm path
  -> layout JSON file for a virtual desktop GUID
     -> display-topology variants
```

Child rows are saved `variants` from `%APPDATA%\DeskRealm\icon-layouts\<desktop-guid>.json`. Rows show each monitor working area separately and mark the primary display.

Lock scopes:

```text
lockedIconLayouts[virtualDesktopGuid]
lockedRealms[normalizedRealmPath]
lockedIconLayoutVariants[virtualDesktopGuid|displayTopologyKey]
```

Realm locks are inherited by child rows and disable child lock/delete actions while keeping the row readable.

## Locked save behavior

Unlocked automatic saves use the normal save-if-changed path.

Locked automatic saves use `save-locked-merge-new-icons`:

- existing saved icon positions are not overwritten;
- new icons absent from the saved layout can be appended once;
- full overwrite remains manual and confirmation-gated.

This avoids silent layout contamination while still allowing the user to add a new shortcut to a locked realm.

## Variant deletion

The variant `Delete` action is destructive only for DeskRealm metadata:

- confirmation is required;
- Desktop files/icons/shortcuts are not deleted;
- the selected `variants[]` entry is removed from the layout JSON;
- matching `lockedIconLayoutVariants` config entry is removed;
- empty layout JSON files are deleted;
- if variants remain, the newest remaining variant is promoted to the legacy/current top-level fields.

## Documentation alignment

Updated release documentation:

- `README.md`
- `CHANGELOG.md`
- `docs/release-notes/v0.5.9.md`
- `docs/patch-notes/PATCH_NOTES_v0_5_9.md`
- `docs/CONFIGURATION.md`
- `docs/INSTALLATION.md`
- `docs/ARCHITECTURE.md`
- `docs/SAFETY_AND_PRIVACY.md`
- `SMOKE_TEST.md`
- `TODO.md`

Transient block TODO files from the iterative `v0.5.9` implementation were consolidated into `TODO.md` for a cleaner release tree.

## Validation status

Static repository validation completed in this environment:

- `CHANGELOG.md` contains a release-helper-compatible `## v0.5.9` section.
- `docs/release-notes/v0.5.9.md` exists and documents the user-facing release.
- `docs/patch-notes/PATCH_NOTES_v0_5_9.md` exists and documents the technical delta.
- Project version references are aligned to `0.5.9`.
- Config version references are aligned to `10`.
- Root package folder is expected to be `DeskRealm/`.

Windows build/runtime validation must still be performed locally with:

```powershell
.\scripts\Build-Release.ps1
```

Expected result: release build succeeds without warnings.

## Recommended manual smoke tests

- Fresh first-run onboarding opens before the first automatic switch.
- Association of original Desktop does not move files.
- Skip path creates original Desktop shortcuts.
- UI opens from tray and hides to tray on close.
- **Quit DeskRealm** exits the app.
- Hotkey capture records `Win+Ctrl+G` when `G` is pressed.
- Modifier-only hotkey capture cancels on release.
- `Win` -> `G` -> `Ctrl` records `Win+G` and ignores the later `Ctrl`.
- Paused automation ignores DeskRealm desktop hotkeys and refuses manual realm switching.
- Icon Layout tab shows saved variants, per-monitor working areas and primary markers.
- Layout/realm/variant locks protect autosaves.
- Variant deletion removes only the saved layout variant.
