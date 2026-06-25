# DeskRealm v0.7.0 — Startup visibility & lock-state clarity (`_bi`)

## Added

- **Start minimized** toggle in Automation with visible-window and notification-area modes.

## Changed

- Config schema advanced to v18. Existing users keep the historical minimized-to-tray launch behavior.
- First-run Desktop association stays visible even when Start minimized is enabled.
- Realm lock glyphs are state-first: `🔒` means locked; `🔓` means unlocked.

## Safety

- This patch does not change Windows startup registration, Explorer restart behavior, virtual desktop switching, Desktop Known Folder routing, wallpaper application or lock propagation.
