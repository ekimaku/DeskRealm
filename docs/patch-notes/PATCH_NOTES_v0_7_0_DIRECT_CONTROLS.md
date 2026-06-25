# DeskRealm v0.7.0 — Realm Studio Direct Controls & State Propagation (`_be`)

## Scope

- Current-topology icon counts on realm cards.
- Direct wallpaper edit/save bubbles with instant draft preview.
- Windows Registry wallpaper reconciliation into managed DeskRealm previews.
- Direct inline hotkey capture/save/cancel from realm cards.
- Direct realm lock and singleton default-realm actions from realm cards.
- Parent/child lock visibility in the detailed editor.
- Explicit Diagnostics restart action.
- Explorer/Registry refresh debounce for Realm Studio.

## Safety

- Wallpaper reconciliation only reads/imports state; it does not apply a wallpaper, switch desktops, restart Explorer or overwrite an unreadable Windows path.
- Realm lock inheritance never destroys individual child-variant lock preferences.
- Restart DeskRealm is confirmed by the user and starts a replacement process; it does not restart Explorer.

## Validation gate

Windows build/publish, quick actions, reverse wallpaper sync, lock inheritance, default star, inline hotkey capture and controlled restart remain mandatory smoke tests before public v0.7.0 publication.
