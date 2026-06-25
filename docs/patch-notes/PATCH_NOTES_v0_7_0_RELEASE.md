# DeskRealm v0.7.0 — Realm Studio: Your Windows Workspaces, Made Visible

## The short version

Realm Studio is now DeskRealm’s primary user experience. Every Windows virtual desktop becomes a visible workspace card where you can switch realms, see the active icon layout, preview or change its wallpaper, edit the hotkey inline, control protection and select the default startup realm.

## A dashboard built around direct control

- **Wallpaper:** click the preview to stage a new image, preview it immediately, then explicitly save it. Realm Studio also imports readable wallpaper changes made directly in Windows so the preview remains honest.
- **Hotkeys:** edit them inline from the VCard with one-click capture, `Waiting input...`, Reset, Save and Cancel.
- **Protection:** lock or unlock the whole realm from the card. Variant-level protection remains preserved and is clearly shown as inherited when the parent realm is locked.
- **Default realm:** move the single startup default with the star control; the active default is always obvious.
- **Startup visibility:** choose whether DeskRealm opens Realm Studio at startup or starts minimized in the notification area.
- **Layout information:** the icon count now describes the layout that matches the display topology you are using now, not a confusing historical total.

## Windows integration that stays explicit

- Rename a realm and choose whether Task View applies the new Windows name at next reboot or immediately through an explicit Explorer restart.
- Explorer restarts no longer leave DeskRealm without a tray icon.
- Explorer metadata rebuilds no longer create transient `Desktop N` duplicate-realm errors.
- The original physical Desktop folder remains protected when its realm is renamed.

## Reliability and safety

- The app continues to use explicit user-visible errors instead of silent fallback behavior.
- No telemetry or remote service was added.
- Build and release scripts no longer depend on candidate-specific source repair or textual implementation checks.
