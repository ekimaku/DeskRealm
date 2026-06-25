# DeskRealm roadmap after v0.7.0

This roadmap is intentionally milestone-based. DeskRealm changes Explorer, the Desktop Known Folder and virtual-desktop metadata, so safety, testability and diagnosability have priority over stacking new convenience features.

## v0.8.0 — Core testability and service boundaries

**Goal:** make future Windows-shell changes safer to evolve.

- Create a focused .NET test project for pure domain logic: GUID normalization, realm-name availability, archive resolution, lock inheritance, default-realm selection, hotkey parsing and config migrations.
- Extract pure decision logic from `DesktopSwitchService` into cohesive services such as realm assignment, rename/archive resolution, protection-state resolution and configuration migration.
- Keep Explorer, registry and Shell COM operations behind narrow adapter boundaries so unit tests do not need a live Windows desktop.
- Add migration fixtures for v11-or-older configuration files and regression tests for one-time `desktopHotkeys` → GUID hotkey conversion.
- Keep runtime behavior unchanged unless a test exposes a real defect.

**Exit gate:** unit tests run locally and in GitHub Actions; the existing Windows smoke matrix remains green.

## v0.9.0 — Diagnostics and supportability

**Goal:** make a rare Windows/Explorer failure understandable without exposing private desktop data by default.

- Add an explicit Diagnostics health view: registry monitor, icon worker, tray registration, current realm, last completed switch and last failure.
- Add non-blocking InfoBars for recoverable UI refresh delays instead of generic blocking error dialogs.
- Add a user-triggered sanitized support bundle: redact user-profile paths, keep relevant version/Windows/build state, include selected logs only after review.
- Add controlled actions with explicit outcomes: copy diagnostics, reopen Realm Studio, restart DeskRealm and open log location.
- Add a concise compatibility snapshot for the active Windows build, display topology and OneDrive Desktop state.

**Exit gate:** a user can report a problem with one action and a maintainer can distinguish Shell readiness, config, tray, registry and icon-worker failures from the report.

## v1.0.0 — Distribution, upgrade and compatibility hardening

**Goal:** declare a stable public Windows utility contract.

- Exercise clean install, upgrade from v0.6.0/v0.7.0, uninstall and emergency restore paths on copies of user state.
- Publish a documented compatibility matrix for supported Windows 10/11 builds, single/multi-monitor setups, DPI changes, OneDrive Desktop backup and Explorer restart behavior.
- Add automated release SHA-256 verification guidance and evaluate code signing separately from feature work.
- Complete keyboard/DPI/accessibility sweep for Realm Studio at 100%, 125%, 150% and 200% scaling.
- Freeze a support policy for configuration migrations and archived realm retention.

**Exit gate:** reproducible release package, upgrade safety verified, diagnostic support path documented and public compatibility scope explicit.

## Later — product scope only after v1.0.0 stability

Potential future ideas are deliberately deferred until the core is well-tested:

- optional realm templates/onboarding presets;
- export/import of DeskRealm-only metadata;
- richer archived-realm browser;
- selective per-realm automation rules;
- localization architecture, if DeskRealm later needs non-English UI.

No private virtual-desktop COM dependency is planned. DeskRealm keeps the Windows-first approach: documented/public APIs where available, controlled Explorer metadata handling where already validated, and explicit user-visible behavior when Windows requires a shell refresh.
