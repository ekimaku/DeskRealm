# DeskRealm smoke test — v0.7.0 development

## Build and launch

```powershell
.\scripts\Build-Release.ps1
.\dist\DeskRealm\DeskRealm.App.exe
```

Expected: the script reaches restore, Release build and self-contained `win-x64` publish without source-repair or text-preflight steps. `dist\DeskRealm\DeskRealm.App.exe` must exist and launch.

## Configuration upgrade migration

- [ ] Copy a v11-or-older development config before testing.
- [ ] Start DeskRealm and confirm legacy number-keyed hotkeys are attached to the intended live Windows desktop GUIDs.
- [ ] Confirm the resulting config uses `realmHotkeys`, has schema version `18`, and no longer writes `desktopHotkeys`.
- [ ] Confirm no Desktop files/folders move during that migration.

## Tray and startup

- [ ] DeskRealm appears in the notification area after startup.
- [ ] Left click / double click opens Realm Studio.
- [ ] Right click shows the tray menu.
- [ ] **Quit DeskRealm** exits the process and removes the tray icon.

## Realm switching and layouts

- [ ] Switch Personal → Work → native Desktop → Personal with configured hotkeys.
- [ ] Verify the correct Desktop folder and wallpaper appear per realm.
- [ ] Verify layout restoration completes without cross-realm icon contamination.
- [ ] Verify a display-topology change does not crash the app.

## Rename behavior

- [ ] Save a realm without changing its name: no Explorer-apply modal appears.
- [ ] Rename a realm to a unique name: choose **Apply and restart Explorer**.
- [ ] Confirm taskbar/Desktop/File Explorer briefly restart, then the DeskRealm tray icon returns automatically.
- [ ] Confirm no **snapshot failed** dialog or false `Desktop 1` / duplicate realm diagnostic appears while Explorer rebuilds.
- [ ] Verify the new name appears in Win + Tab.
- [ ] Rename another realm with **Apply on next reboot**; verify the name after reboot.
- [ ] Confirm the remembered rename-apply preference can be changed in Automation.

## Explorer restart reconciliation

- [ ] With several existing realms, rename any realm and choose **Apply and restart Explorer**.
- [ ] Wait for the taskbar and DeskRealm tray icon to return.
- [ ] Confirm Realm Studio refreshes without creating a new `Desktop N` realm folder or reporting an assignment collision from a removed desktop GUID.
- [ ] Confirm deleted-desktop configuration metadata is preserved as an archived profile and does not block a live realm name.

## Name conflicts and archived realms

- [ ] Enter a name owned by an active desktop: Save is disabled and the owning live desktop is shown inline.
- [ ] Create an archived test profile, then rename a new desktop to that archived name.
- [ ] With **Ask before reusing an archived realm**, verify **Reuse archived layout**, **Start fresh** and **Cancel** are offered before mutation.
- [ ] Verify direct reuse/start-fresh choices persist and do not fall through to an active-name conflict error.

## v0.7.0 `_bf` — Direct Controls Compile Repair

1. Run `./scripts/Build-Release.ps1` from a clean extraction.
2. Confirm both the regular Release build and `win-x64` publish complete.
3. Open **Edit** for a realm with an assigned hotkey; confirm the editor preloads the actual shortcut.
4. Open **Edit** for a realm without a hotkey; confirm the editor starts empty and never writes `Not assigned` as a shortcut.

## v0.7.0 `_bh` — Inline Hotkey Layout & First-Click Repair

1. On a Realm VCard, click the hotkey **✏️** once. Confirm the display is replaced immediately by a focused full-width field reading **Waiting input...** before any key is pressed.
2. Confirm the lower action row contains **Reset**, **💾** and **×**; no capture description or status text should consume tile width after a valid chord is captured.
3. Capture a valid chord and click **💾**. Confirm it is globally registered. Start another edit, change the draft, click **Reset**, and confirm the value returns to the hotkey that existed when the editor opened.
4. Repeat with **×** and verify the prior binding remains. Verify Backspace/Delete only clear the current draft and are not saved until **💾** is clicked.
5. Open the full Realm Editor and confirm its hotkey field still starts capture only after the user clicks the field.

## Realm Studio direct controls and propagation

- [ ] In **Realms**, verify each card shows the icon count for the current display-topology variant. A realm with historical variants but no current-topology snapshot must show **No current layout snapshot**.
- [ ] Select a wallpaper directly in Windows for a realm, open or refresh Realm Studio, and confirm DeskRealm imports a readable changed image into its managed wallpaper store and refreshes the card preview without switching desktops or restarting Explorer.
- [ ] On a card, click **✏️** on the wallpaper preview, select an image, confirm immediate draft preview, then click **💾** and verify the managed copy / preview remains after Refresh.
- [ ] On a card, click **✏️** for Hotkey, capture a chord, click **💾**, then verify the new chord works. Repeat with **×** and confirm the old mapping remains.
- [ ] Toggle the card realm lock. Open the editor and confirm every saved child variant is marked **Locked by realm** and its individual lock/delete action is disabled. Unlock the realm and confirm prior individual variant locks return.
- [ ] Toggle the current-layout lock in the editor and confirm only the current topology shows **Locked by current layout**.
- [ ] Click **⭐** on a non-default card and confirm it becomes the sole **🌟** default card. Confirm the former default returns to **⭐**.
- [ ] Confirm the action label is **Switch**, and that Switch does not use a stale icon-count total.
- [ ] In Diagnostics, choose **Restart DeskRealm**, approve the confirmation, then confirm a replacement process returns with tray access and working hotkeys. Repeat once from `Run-DeskRealm.ps1` if practical.

## Native Desktop safety

- [ ] Edit the realm mapped to the original Windows Desktop.
- [ ] Change its Windows virtual-desktop name.
- [ ] Confirm the physical `C:\Users\<user>\Desktop` path and files are unchanged.

## Final release gate

- [ ] Re-run `Build-Release.ps1` from a clean checkout.
- [ ] Run the portable EXE from `dist`.
- [ ] Close DeskRealm through the tray and confirm no background process remains.

## v0.7.0 `_bi` — Startup visibility and lock-state clarity

1. In **Automation**, set **Start minimized** to **Show Realm Studio**, apply settings, choose **Restart DeskRealm**, and confirm the replacement process leaves Realm Studio visible.
2. Set **Start minimized** to **Notification area**, apply settings, restart DeskRealm, and confirm the process starts with tray access but without an open Realm Studio window.
3. Confirm the first-run Desktop import modal remains visible regardless of the saved Start minimized preference on a fresh config.
4. Confirm the Automation **Start DeskRealm with Windows** toggle remains independent from Start minimized.
5. On an unlocked realm card, confirm the bubble displays `🔓` and its tooltip says that clicking locks the realm. Lock it and confirm the bubble becomes `🔒`, the status reads **Realm locked**, and the tooltip says that clicking unlocks the realm.
