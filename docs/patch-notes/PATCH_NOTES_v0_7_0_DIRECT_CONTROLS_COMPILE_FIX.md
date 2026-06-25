# DeskRealm v0.7.0 — Direct Controls Compile Fix (`_bf`)

## Fixed

- `RealmEditorModal` referenced `RealmCardViewModel.Hotkey`, but the view model exposed only `HotkeyDisplay`. The standard Windows build therefore stopped with `CS1061` before the XAML compiler could complete.
- `RealmCardViewModel` now exposes `Hotkey` as the raw nullable stored value. `HotkeyDisplay` remains the presentation string for cards.

## Safety

- No realm, wallpaper, hotkey registration, lock, default-realm or Explorer behavior changed.
- The fix prevents the UI text `Not assigned` from ever being supplied to the hotkey capture control as though it were an actual shortcut.

## Validation required

- Run `./scripts/Build-Release.ps1` on Windows.
- Open the unified editor for a realm with a shortcut and one without a shortcut.
