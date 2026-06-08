# Changelog

## v0.5.0 - Open-source publication package

### Added

- GitHub-ready project metadata: Apache-2.0 license, NOTICE, CITATION.cff, contributor docs, security policy and issue templates.
- End-user installation documentation.
- GitHub Actions workflow for Windows build artifacts and tag-based releases.
- Portable `win-x64` release ZIP packaging.
- Transparent PowerShell install/uninstall bundle packaging.

### Notes

- The first public release is designed to be source-first and transparent. A signed MSI/EXE installer may be added later if the project grows.


## v0.5.0 — Open-source publication package

- Added Apache-2.0 `LICENSE`.
- Added `NOTICE` with attribution notice.
- Added `CITATION.cff` for GitHub citation support.
- Added `AUTHORS.md`, `THIRD_PARTY_NOTICES.md`, `CONTRIBUTING.md`, `SECURITY.md`, and `CODE_OF_CONDUCT.md`.
- Added GitHub issue/PR templates.
- Added documentation: architecture, configuration, safety/privacy, attribution, references, and release checklist.
- Moved previous patch notes to `docs/patch-notes/`.
- Updated project metadata for open-source packaging.
- No runtime behavior change from v0.4.1.

## v0.4.1 — SendInput hotkey fix

- Fixed native `INPUT` structure for `SendInput`.
- Added `hotkeyInitialDelayMs` to avoid modifier collision while the registered hotkey is still physically held.

## v0.4.0 — Hotkeys and Windows startup

- Added configurable direct desktop hotkeys.
- Added tray toggle for startup with Windows.
- Added hotkey reload from config.

## v0.3.3 — Icon identity fix and autosave

- Fixed icon identity/position mismatch by using a PIDL-derived item key.
- Added icon layout autosave with change detection.

## v0.3.2 — Shell icon worker hardening

- Replaced fragile `STRRET` display name path.
- Added capture/restore phase logging.

## v0.3.1 — Icon crash guard

- Isolated icon layout Shell/COM operations in a worker process.
- Main tray process remains alive if the worker fails.

## v0.3.0 — Icon layouts per realm

- Added save/restore of Desktop icon positions per virtual desktop GUID.

## v0.2.0 — Task View names to folders

- Synced realm folder names from Windows Task View virtual desktop names.
- Renamed existing folders instead of duplicating them.

## v0.1.3 — Robust current desktop registry detection

- Added multiple registry lookup paths for `CurrentVirtualDesktop`.

## v0.1.2 — Stable ZIP root

- ZIP root folder changed to `DeskRealm/`.

## v0.1.1 — CS0199 build fix

- Fixed readonly GUID passed by `ref` to P/Invoke.

## v0.1.0 — Native desktop switch prototype

- Initial tray app.
- Desktop Known Folder switching by virtual desktop.
- Restore path and emergency restore script.