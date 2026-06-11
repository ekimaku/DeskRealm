# Changelog

Current stable: `v0.5.7`.

## v0.5.7 — First-run Desktop import wizard

### Added
- Added a first-run Desktop import wizard for new installations.
- The wizard can assign the current Windows Desktop to a chosen virtual desktop realm before the first automatic switch.
- The wizard can optionally move existing Desktop files and shortcuts into the selected realm.
- The wizard can optionally save the currently visible icon positions as that realm's initial layout.
- Added strict conflict checks for initial import: no silent overwrite, no hidden merge, and `desktop.ini` / DeskRealm's own realms root are skipped.
- Added config version `5` with first-run import settings.

### Changed
- Existing upgraded installations are marked as import-completed during migration so the wizard does not unexpectedly interrupt users after an update.
- Documentation, release notes, smoke tests and release checklist now describe the v0.5.7 onboarding flow while retaining the validated v0.5.6 icon layout engine.

### Notes
- The icon layout engine remains the v0.5.6-stabilized model: display-topology variants, DPI/scale awareness, delayed/verified restore, and Shell identity fallback for repeated shortcuts.

## v0.5.6 — Shell identity fallback for repeated icons

- Stores Shell display/parsing identity metadata for desktop icons in addition to the PIDL-derived key.
- Restores icons through exact PIDL first, then falls back to secondary Shell identity matching when Explorer exposes the same shortcut with a different PIDL after a realm/display transition.
- Adds explicit fallback-match logs so icons like browser shortcuts can be diagnosed by human-readable names instead of only `Desktop item #...` placeholders.
- Keeps the existing anti-contamination and verified restore logic from v0.5.4/v0.5.5.

## v0.5.5 — Verified chunked icon placement

- Applies icon positions in smaller chunks instead of one large Shell batch.
- Verifies actual icon positions after restore and retries only icons that did not move.
- Keeps `SVSI_NOTAKEFOCUS` during placement to reduce Shell focus side effects.
- Adds logs for unresolved icons after restore verification so persistent identity issues can be diagnosed without silent fallback.

## v0.5.4 — Deferred switch restore and batched icon placement

- Deferred icon layout restore after a desktop switch so Explorer has time to finish showing the target realm before DeskRealm moves icons.
- Added a pending-restore save guard that refuses icon saves while a switch restore is waiting, preventing transient previous-realm icons from contaminating the target realm layout.
- Added restore retries after switch to stabilize icons if Explorer reflows shortly after the first placement pass.
- Changed icon restore to position all matching icons in a single batched `IFolderView.SelectAndPositionItems` call instead of moving icons one by one.
- Added config migration v4 for switch restore delay and retry settings.

## v0.5.3 — Display topology aware icon layouts

- Added display topology variants for icon layouts, keyed by active monitor set, virtual bounds, resolution, orientation and effective DPI / scale.
- Added per-icon screen-relative metadata so best-effort restores can adapt positions when resolution or scale changes.
- Added a display topology save guard that skips saves while monitor/resolution/DPI changes are settling, preventing Windows-compacted positions from contaminating a realm.
- Added automatic restore of the current realm after a display topology change settles.
- Enabled PerMonitorV2 DPI awareness so DeskRealm can see per-monitor scale values for layout keys.

## v0.5.2 — Icon layout save guard

- Prevented icon layout saves when the active Windows virtual desktop does not match the DeskRealm realm currently assigned as the known Desktop folder.
- Fixed cross-desktop icon position contamination when identical icons exist on multiple realms with different positions.
- Kept pre-switch saves for DeskRealm hotkey navigation, where the previous desktop is still active and safe to capture.
- Added explicit refusal for manual icon layout saves when the known Desktop path and current virtual desktop are out of sync.

## v0.5.1 — Quiet icon layout hotfix

- Disabled background icon layout autosave by default to avoid periodic cursor busy-state flicker.
- Added config migration from v1 to v2 that disables old periodic autosave settings.
- Preserved automatic layout saving on desktop switch and before exit restore.
- Removed post-restore Shell refresh after icon placement.
- Updated docs to describe the quiet save model.

## v0.5.0 — Open-source publication package

- Added Apache-2.0 `LICENSE`.
- Added `NOTICE` with attribution notice.
- Added `CITATION.cff` for GitHub citation support.
- Added `AUTHORS.md`, `THIRD_PARTY_NOTICES.md`, `CONTRIBUTING.md`, `SECURITY.md`, and `CODE_OF_CONDUCT.md`.
- Added GitHub issue/PR templates.
- Added documentation: architecture, configuration, safety/privacy, attribution, references, release checklist, installation and release process.
- Added GitHub Actions workflow for Windows build artifacts and tag-based releases.
- Added portable `win-x64` release ZIP packaging.
- Added transparent PowerShell install/uninstall bundle packaging.

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
