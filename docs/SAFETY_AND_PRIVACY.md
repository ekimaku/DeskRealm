# Safety and privacy

## Desktop safety

DeskRealm changes the current user’s Windows Desktop Known Folder to switch visible Desktop content by virtual desktop. This is powerful and should be used carefully:

- keep a backup or ZIP of your project state before development candidates;
- extract candidates into fresh folders rather than overlaying files;
- avoid testing against irreplaceable Desktop-only files;
- review OneDrive Desktop backup behavior before use;
- use `Restore-Desktop.ps1` from a portable build if you need to return the Desktop Known Folder to its original path.

## Realm rename safety

A virtual-desktop rename persists metadata in Explorer’s per-user state. To make Task View show it immediately, the user can explicitly request an Explorer restart. That briefly restarts the taskbar, Desktop and File Explorer windows. DeskRealm never performs that restart silently.

The original/native Windows Desktop assignment is protected: DeskRealm may change the virtual desktop’s visible label, but never renames, moves or remaps the physical Desktop folder.

## Privacy

DeskRealm runs locally. Realm settings, wallpaper copies, layout data and logs stay in the current Windows user profile. The project does not require cloud sync or a DeskRealm server.


## Configuration upgrade safety

Schema v17 removes retired compatibility settings from the next saved configuration file. It does not move Desktop files, delete realm folders or change the original Desktop assignment. For an upgrade test from an older development candidate, copy the config file first so you can inspect or restore the pre-migration state.


## Explorer-restart reconciliation safety

At DeskRealm startup, the application may retire configuration entries for virtual-desktop GUIDs that Windows no longer reports as live. This changes only DeskRealm configuration: it does not delete, move, rename or merge a realm folder, a saved icon layout file or a managed wallpaper. The retained data is represented as archived metadata so a later reuse remains an explicit user decision.

## Wallpaper reconciliation and restart safety

Realm Studio may read the current user's virtual-desktop wallpaper Registry value during a dashboard refresh. When the referenced source is readable and differs from the managed wallpaper asset, DeskRealm copies it into `%APPDATA%\DeskRealm\wallpapers` so the preview remains stable. It does not alter that Windows setting during the read/import path. A missing or unreadable Registry target is shown as unavailable rather than silently replaced.

**Restart DeskRealm** starts a replacement process only after explicit confirmation. The replacement waits for the current DeskRealm process to release its single-instance guard. Restarting DeskRealm is distinct from restarting Explorer; it does not touch the taskbar, Desktop shell or Windows virtual-desktop names.

A realm lock changes DeskRealm protection metadata only. It never modifies or deletes a saved icon-layout file merely because a parent lock is enabled or disabled.


## v0.7.0 `_bf` — Hotkey safety

DeskRealm distinguishes an absent hotkey from user-facing display text. The display fallback `Not assigned` is never persisted or registered as a global hotkey.

## v0.7.0 `_bh` — Inline hotkey safety

`Waiting input...` is only an ephemeral capture state. It cannot be saved or registered as a global shortcut. The VCard **Reset** button restores the original assigned value for the open edit session and does not persist or re-register a binding. **💾** remains the sole explicit inline commit; **×**, Escape, focus loss and modifier release preserve the previously persisted hotkey. Backspace/Delete can clear only the current draft and still require **💾** to commit.

## Startup visibility

**Start minimized** changes only whether the Realm Studio window is shown after DeskRealm starts. It does not start DeskRealm without user configuration, alter Windows Run-key registration, change Explorer, switch a desktop, or perform a background mutation. The first-run Desktop association safety gate remains visible in every startup mode.
