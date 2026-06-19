# Changelog

Current public release: `v0.6.0`.

Previous `0.5.x` builds are superseded by `v0.6.0` and are kept only as historical changelog entries.

## v0.6.0 — Adaptive performance pipeline

### Added
- Added registry-driven virtual desktop change monitoring with explicit primary-observer failure reporting.
- Added one serialized background execution lane shared by tray, hotkeys, registry reconciliation and main-window switch/save/restore/lock actions so the WinForms UI remains responsive and state mutations cannot race.
- Added physical modifier release detection before DeskRealm emits virtual desktop navigation input.
- Added per-step virtual desktop GUID confirmation for direct desktop hotkeys.
- Added a parallel hotkey transaction pipeline: after the source layout is saved and modifiers are released, Windows navigation and destination realm loading/restoration start together.
- Added a persistent STA icon-layout worker with a strict GUID-correlated JSON-lines protocol over BOM-free UTF-8.
- Added adaptive Shell-view readiness checks requiring exact target-realm membership before icon restore starts.
- Added adaptive icon-position verification and progress-based reapplication.
- Added `[PERF]` phase timings and coalesced registry-notification diagnostics.
- Added a release-control validation document summarizing package, documentation, versioning and remaining Windows checks.
- Added `VERSION.txt` and a pinned .NET 10 SDK policy through `global.json`.

### Changed
- Migrated the source project from .NET 8 to .NET 10 / C# 14 while preserving self-contained `win-x64` release output.
- Replaced periodic virtual desktop polling with registry notifications.
- Replaced fixed switch, settle and retry delays with state confirmation and bounded adaptive backoff.
- Reused one icon worker for the session instead of starting one process per save or restore.
- Changed direct hotkeys so the pre-resolved destination realm no longer waits for the final navigation GUID before its Known Folder, Shell notification and adaptive layout restore begin.
- Replaced the global Shell broadcast refresh with a targeted directory notification.
- Deferred hidden-window UI refresh until the DeskRealm window is opened again.
- Config schema advanced to version `11`; retired timing fields are removed during migration.
- WinForms custom-control properties now declare explicit designer serialization behavior required by current analyzers.
- Documentation now includes a clear v0.6.0 performance-gains section for users and reviewers.

### Fixed
- Fixed reboot/logon recovery when Windows starts DeskRealm while the Desktop Known Folder already points to the current realm. The first reconciliation now performs one adaptive layout restore instead of returning early with `Already on`, so Explorer startup compaction cannot silently become the visible session layout.
- Fixed confirmation-gated manual save on locked layouts/realms so it replaces only the exact active display-topology variant. A dedicated worker command preserves and fingerprints every non-current variant before and after JSON serialization; the write is refused if another variant changes.
- Removed silent oldest-variant eviction when a 25th topology would be created. DeskRealm now requires explicit deletion of an obsolete variant instead of modifying unrelated saved layouts.
- Prevented a changing Explorer Desktop view from aborting startup layout persistence with `E_BOUNDS` / `E_CHANGED_STATE`; visible PIDLs are now enumerated through `IFolderView::Items` / `IEnumIDList`, and transient view invalidation is retried inside the existing adaptive readiness deadline.
- Prevented a two-or-more-desktop jump from disabling layout persistence when `IFolderView::GetItemPosition` returns transient `E_FAIL` while Explorer replaces the active view. `E_FAIL` is retried only inside the automatic transition-aware restore path; manual save/restore operations remain strict.
- Prevented UTF-8 BOM bytes from contaminating the first persistent-worker JSON command.
- Prevented Explorer's partially transitioned icon list from being accepted as the target realm.
- Prevented registry notification storms from scheduling/logging one reconciliation per raw signal.
- Preserved the existing branded icon, onboarding, hotkey capture, variant deletion and multi-display metadata while replacing the fragile runtime path.

### Safety
- Hotkey navigation and target preparation join at a final GUID barrier. If Windows lands on another desktop, DeskRealm explicitly discards the speculative target and restores the realm/layout of the desktop actually active before reporting the mismatch.
- No silent fallback to polling or worker restarts is introduced.
- A worker/protocol/restore failure disables icon persistence for the current session and reports the degraded state explicitly; desktop realm switching remains alive.
- Existing realm, layout and exact topology-variant locks remain enforced.
- Manual overwrite confirmation authorizes only the current topology variant; it never authorizes bulk replacement or implicit eviction of other variants.

## v0.5.9 — First-run UX, modern UI and layout controls

### Added
- Added the main DeskRealm window, opened from the tray or tray icon double-click, while keeping the default runtime tray-first.
- Added first-run onboarding inside the main window so fresh installs explain DeskRealm before the first automatic Desktop switch.
- Added a safe first-run choice to associate the original Windows Desktop with one realm without moving files.
- Added a skip path that creates `DeskRealm - Original Desktop.lnk` shortcuts inside managed realms so the original Desktop remains easy to find.
- Added a modern dark/cyan DeskRealm UI shell with custom navigation, rounded cards, custom buttons, status pills and dark in-app window chrome.
- Added the DeskRealm `DR` logo as the compiled executable icon, tray notification icon and main window icon.
- Added UI access to the main tray actions: refresh, sync names, save/restore icon layout, restore original Desktop, startup toggle, open realms/config/logs and quit.
- Added capture-based hotkey fields in the **Hotkeys** tab. Click a field, hold one or two modifiers, then press the main key to record the shortcut.
- Added the dedicated **Icon Layout** tab with collapsible realm rows and child rows for saved display-topology layout variants.
- Added layout locks, realm locks and variant locks from the UI.
- Added locked-layout autosave merge mode: existing icon positions stay protected, while newly added icons can be captured once.
- Added confirmation-gated manual overwrite for locked layouts.
- Added confirmation-gated `Delete` for saved layout variants. Deleting a variant removes only DeskRealm layout metadata, never Desktop files or icons.

### Changed
- Closing the DeskRealm window with the cross now hides it back to the tray. **Quit DeskRealm** is the explicit app exit.
- Default desktop hotkeys now avoid common Windows/app conflicts: desktop 1-4 use `Win+Shift+X`, `Win+Shift+C`, `Win+Shift+B`, `Win+Shift+N`.
- Existing customized hotkeys are preserved during migration; only untouched legacy defaults are replaced.
- `enabled` is now presented as **Enable realm switching automation**. When disabled, automatic realm switching and DeskRealm desktop hotkeys are paused without deleting data.
- Hotkey capture now stops immediately on the first non-modifier key. Releasing only modifier keys cancels capture and restores the previous value.
- The **Icon Layout** tab now shows saved `variants` from each layout JSON file instead of a single combined layout row.
- Variant rows show each persisted display working area separately using `DisplayX.workingWidth` / `DisplayX.workingHeight`, with `✅` marking the primary display.
- Config version is now `10`, adding lock dictionaries for layouts, realms and exact layout variants.
- Realm locks are stored by normalized realm path so the lock applies to every desktop/layout assigned to that realm.
- Variant locks are stored as `{virtual-desktop-guid}|{display-topology-key}`.
- DeskRealm public UI strings remain English.
- The tray icon now loads the embedded application icon from the compiled executable instead of the generic Windows application icon.

### Fixed
- Fixed ambiguous pause behavior where DeskRealm desktop hotkeys could still switch realms while DeskRealm was disabled.
- Fixed hotkey capture so later modifier keys cannot be appended after a main key has already been recorded.
- Fixed modifier-only capture attempts so they cancel cleanly when modifiers are released.
- Fixed visual artifacts around custom buttons by replacing native `Button` rendering with a pure owner-painted control.
- Fixed clipped text, clipped child-row borders and residual WinForms repaint artifacts in the modern UI.
- Fixed the bright native title bar by replacing it with custom dark in-app chrome.
- Fixed nullable WinForms font construction warnings in the Windows release build.

### Safety
- The first automatic Desktop switch is delayed until the first-run decision is completed.
- First-run onboarding no longer moves Desktop files. Association points the selected realm to the original Desktop path.
- Shortcut creation failures are explicit and do not silently complete onboarding.
- Locked layouts refuse silent overwrite. Autosave can only merge new icons; full overwrite requires confirmation.
- Locked realms protect all child layout variants assigned to that realm and disable child destructive actions.
- Variant deletion is scoped to DeskRealm metadata and requires confirmation.

### Documentation
- Updated README, CHANGELOG, release notes, patch notes, installation, configuration, architecture, safety, smoke test, TODO and technical audit for the full `v0.5.8` -> `v0.5.9` delta.
- Documented the branded executable/tray icon integration in the release documentation.

## v0.5.8 — Safe first-run Desktop association

### Fixed
- Reworked first-run onboarding so DeskRealm no longer moves files from the original Windows Desktop into a managed realm folder.
- The selected virtual desktop realm now points directly to the original Desktop folder, preserving the normal Desktop when DeskRealm is closed or disabled.
- Added absolute-path realm assignments so a realm can safely use an existing external folder such as the original Desktop.
- Prevented duplicate assignment of the original Desktop path to multiple virtual desktops.

### Changed
- The first-run wizard now describes the safe association model instead of file migration.
- Config migration v6 disables the legacy `initialDesktopImportMoveFiles` flag.
- Documentation, installation notes, safety notes and release process now describe the no-move onboarding model.

### Safety
- DeskRealm must not silently move user Desktop files during onboarding. Existing files remain in the original Desktop folder.

## v0.5.7 — First-run Desktop import wizard

### Added
- Added a first-run Desktop import wizard for new installations.
- The wizard can assign the current Windows Desktop to a chosen virtual desktop realm before the first automatic switch.
- Superseded by v0.5.8: the wizard no longer moves Desktop files; it now links the selected realm to the original Desktop path.
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
- Added a display topology save guard that skips saves while monitor/resolution/DPI changes are settling.

## v0.5.2 — Icon layout persistence

- Added per-desktop icon layout save/restore.
- Added isolated icon-layout worker process for Shell/COM resilience.
- Added manual tray actions to save and restore icon layouts.

## v0.5.1 — Release automation and tray polish

- Added release helper and GitHub release workflow documentation.
- Added tray actions for startup, logs/config access and safer runtime control.

## v0.5.0 — First public open-source release

- Initial public DeskRealm release.
- Per-Windows-virtual-desktop Desktop folder switching.
- Realm folder creation, assignment and Task View name sync.
- Tray-first Windows utility model.
