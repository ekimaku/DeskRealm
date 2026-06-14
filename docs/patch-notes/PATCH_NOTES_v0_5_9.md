# Patch notes — DeskRealm v0.5.9

These notes document the implementation delta from `v0.5.8` to `v0.5.9`. Public release notes are in `docs/release-notes/v0.5.9.md`; the release helper uses the `CHANGELOG.md` `## v0.5.9` section as the GitHub Release body source.

## User-facing UX

- Added `DeskRealmMainForm`, a tray-opened WinForms UI for onboarding, hotkeys, Icon Layout management, actions/options and status.
- Kept normal runtime tray-first: the UI is hidden by default outside first run.
- Added close-to-tray behavior on the custom close button / window close.
- Added explicit **Quit DeskRealm** action in the UI.
- Added UI equivalents for important tray actions.
- Replaced the classic WinForms tab strip with a modern in-window navigation shell.
- Replaced the native bright title bar with custom dark in-app chrome.
- Rebuilt modern buttons as pure owner-painted controls to avoid residual native button artifacts.
- Kept DeskRealm public UI strings in English.

## Branding and app icon

- Added `src/DeskRealm.App/Assets/DeskRealm.ico` as the Windows application icon.
- Added `src/DeskRealm.App/Assets/DeskRealm.png` as the generated transparent logo asset used for the icon export.
- Set `<ApplicationIcon>Assets\DeskRealm.ico</ApplicationIcon>` in `DeskRealm.App.csproj` so the compiled release `.exe` carries the DeskRealm logo.
- Added `DeskRealmIcon` to load the embedded executable icon for the tray notification icon and main window.
- Replaced the generic `SystemIcons.Application` tray icon with the embedded DeskRealm icon.

## First-run onboarding

- Added first-run explanation inside the main UI.
- Delayed the first automatic polling/switch loop until onboarding is completed.
- Kept the v0.5.8 safe association model: original Desktop association uses an absolute path and does not move files.
- Added skip flow that creates `DeskRealm - Original Desktop.lnk` shortcuts in managed realms.
- Shortcut creation failures remain explicit and do not silently complete onboarding.

## Hotkeys

- Changed default desktop hotkeys for desktops 1-4 to:
  - `Win+Shift+X`
  - `Win+Shift+C`
  - `Win+Shift+B`
  - `Win+Shift+N`
- Migration preserves existing customized hotkeys and only replaces untouched legacy defaults.
- Added capture-based hotkey fields.
- Capture accepts one or two modifiers from `Win` / `Ctrl` / `Alt` / `Shift` plus one main key.
- Capture stops immediately when the first non-modifier key is pressed.
- Releasing all modifiers before a main key cancels capture and restores the previous value.
- Pressing a modifier after the main key is ignored because capture has already completed.
- Saved hotkeys are normalized before duplicate detection and registration.

## Realm switching automation pause

- Renamed the ambiguous `DeskRealm enabled` UI label to **Enable realm switching automation**.
- When config `enabled` is `false`, automatic switching is paused.
- DeskRealm desktop hotkeys are ignored while automation is paused.
- `Refresh now` / `SwitchNow()` refuses to switch realms while automation is paused and surfaces the paused state explicitly.
- Pausing automation is non-destructive and does not remove assignments, realm folders, icon layouts or files.

## Icon Layout locks

- Added config dictionaries:
  - `lockedIconLayouts`
  - `lockedRealms`
  - `lockedIconLayoutVariants`
- Added current layout lock/unlock from the UI.
- Added realm lock/unlock from the UI.
- Added variant lock/unlock from the Icon Layout tree.
- Realm locks are keyed by normalized realm path and protect every layout assigned to that realm.
- Variant locks are keyed by `{virtual-desktop-guid}|{display-topology-key}`.
- Added worker operation `save-locked-merge-new-icons`.
- Automatic saves for locked layouts merge only newly detected icons and do not overwrite saved positions.
- Manual overwrite of a locked layout requires confirmation from tray/UI paths.

## Icon Layout tree and variants

- Added a dedicated **Icon Layout** view.
- Added collapsible realm rows.
- Child rows now represent persisted layout JSON `variants`, not fabricated one-per-desktop rows.
- Child rows show `CURRENT` for the active display-topology variant when applicable.
- Child rows are disabled/readable when the parent realm is locked.
- Variant rows display each persisted monitor working area separately using `workingWidth` and `workingHeight` from `DisplayX` topology entries.
- Primary display rows are marked with `✅`.

## Variant deletion

- Replaced the passive `SAVED` pill with a confirmation-gated `Delete` action on saved variants.
- `EMPTY` remains passive for unsaved rows.
- Deletion removes only the selected variant entry from `%APPDATA%\DeskRealm\icon-layouts\<desktop-guid>.json`.
- Desktop files, icons, shortcuts and realm contents are never deleted by variant deletion.
- Matching `lockedIconLayoutVariants` entries are removed when a variant is deleted.
- If the deleted variant was the last variant, the layout JSON file is removed.
- If variants remain, the newest remaining variant is promoted to the legacy/current top-level layout fields for compatibility.
- Parent realm/layout locks disable child destructive actions.

## Configuration and compatibility

- Project version bumped to `0.5.9`.
- Config version bumped to `10`.
- Layout model version remains `3`.
- Existing v0.5.8 absolute-path Desktop association is preserved.
- Existing v0.5.6-v0.5.8 icon layout behavior is preserved for unlocked layouts.
- Existing customized hotkeys are preserved.
- New lock dictionaries default to empty.

## Documentation

- Updated:
  - `README.md`
  - `CHANGELOG.md`
  - `docs/release-notes/v0.5.9.md`
  - `docs/patch-notes/PATCH_NOTES_v0_5_9.md`
  - `docs/CONFIGURATION.md`
  - `docs/INSTALLATION.md`
  - `docs/ARCHITECTURE.md`
  - `docs/SAFETY_AND_PRIVACY.md`
  - `docs/TECHNICAL_AUDIT.md`
  - `SMOKE_TEST.md`
  - `TODO.md`
