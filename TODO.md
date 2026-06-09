# DeskRealm TODO

## v0.5.1 quiet icon layout hotfix

- [x] Audit cursor flicker cause: periodic icon worker launched by autosave timer.
- [x] Disable background autosave by default.
- [x] Add config migration so existing v0.5.0 installs stop polling automatically.
- [x] Preserve automatic save-on-switch.
- [x] Preserve save-before-exit-restore.
- [x] Remove post-restore Shell refresh after icon placement.
- [x] Update docs/release notes/changelog.
- [x] Run static smoke test in sandbox.

## Future ideas

- Optional signed installer.
- Settings UI for config values.
- More robust diagnostics panel.
## v0.5.2 follow-up

- Validate same-icon/different-position layouts across at least two realms.
- Observe whether manual Windows Task View switching needs a future event-based pre-switch save model.

