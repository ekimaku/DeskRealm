# Safety and privacy

## What DeskRealm changes

DeskRealm changes the current user's Windows Desktop Known Folder path while it is running. This changes what Explorer displays as the Desktop.

It does not intentionally upload or synchronize files. Since `v0.5.8`, first-run onboarding does not move Desktop files.

## What DeskRealm stores

DeskRealm stores local files only:

```text
%APPDATA%\DeskRealm\deskrealm.config.json
%APPDATA%\DeskRealm\icon-layouts\*.json
%LOCALAPPDATA%\DeskRealm\logs\deskrealm.log
```

Icon layout files can contain:

```text
virtual desktop GUIDs
realm names
Desktop icon positions
Shell display/parsing identity metadata for icons
monitor topology metadata: resolution, bounds, orientation, DPI / scale
```

This data stays local and is used only to restore the Desktop view.

## Network behavior

DeskRealm has no network feature and no telemetry.

## OneDrive warning

DeskRealm rejects OneDrive Desktop paths by default because dynamic Desktop Known Folder switching can interact badly with folder synchronization.

You can disable this check manually, but that is not recommended unless you understand the risk.

## First-run safety

On fresh installs, DeskRealm opens onboarding before the first automatic Desktop switch. The user can:

- associate the original Desktop path with a selected realm without moving files;
- skip association and create explicit shortcuts back to the original Desktop inside managed realms.

If shortcut creation fails, DeskRealm shows the error and does not silently complete onboarding.

## UI close behavior

Closing the DeskRealm window with the cross hides it to the tray. This is intentional and prevents accidental shutdown. To stop DeskRealm, use **Quit DeskRealm** in the UI or **Quit** from the tray.

## Display topology safety

DeskRealm tracks display topology to avoid corrupting icon layouts when monitors, resolution or DPI / scale change. It intentionally skips icon-layout saves while such changes are settling.

The Icon Layout UI reads display-topology metadata already stored in local icon layout JSON files. It displays per-monitor working areas and primary-display markers only for local troubleshooting; no display metadata is sent anywhere.

## Layout, realm and variant lock safety

Locked layouts are designed to prevent accidental icon-position drift. Automatic saves do not overwrite existing protected positions. If a new icon appears on a locked layout, DeskRealm may merge that new icon position once so the icon is not lost from the saved layout.

Realm locks are broader than layout locks. A realm lock applies to every layout assigned to that realm path, and the UI disables child layout/variant actions while keeping rows readable.

Variant locks protect exact display-topology variants. When the current variant is locked, automatic saves use merge-only-new-icons behavior; existing positions are not overwritten without explicit confirmation.

A full overwrite of a locked layout is still possible, but only after an explicit confirmation prompt from the UI or tray action.

## Icon layout variant deletion safety

Deleting an icon layout variant is confirmation-gated and scoped to DeskRealm metadata. It deletes the saved layout variant entry only; DeskRealm does not delete Desktop icons, shortcuts, documents, folders, or realm content.

Parent realm/layout locks keep child destructive actions disabled.

## Pause semantics

The **Enable realm switching automation** option is a non-destructive pause. When disabled, DeskRealm does not automatically switch realm folders and DeskRealm desktop hotkeys are ignored.

Existing assignments, realm folders, Desktop files, icons and saved layouts are left untouched.

## Hotkey capture safety

Hotkey capture in the UI only changes the text field until the user clicks **Save + reload**. Capture cancels without saving if the user only presses modifiers and releases them before choosing a main key.

Invalid or duplicate shortcuts are rejected explicitly. DeskRealm does not silently replace them with fallback shortcuts.

## Emergency restore

Run:

```powershell
.\scripts\Restore-Desktop.ps1
```

The script reads `%APPDATA%\DeskRealm\deskrealm.config.json` and restores `originalDesktopPath`.

## Backups

Before first use, back up important Desktop files. DeskRealm is designed not to move/delete files, but it intentionally changes a high-impact Windows Shell setting.

## Branding asset privacy

The branded application/tray icon is bundled inside the DeskRealm app package. Loading the icon does not contact any external service and does not read user files beyond the running executable itself.
