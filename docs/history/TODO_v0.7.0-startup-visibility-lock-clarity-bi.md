# DeskRealm v0.7.0 — Startup visibility & realm-lock clarity (`_bi`)

## Scope

Close two UX ambiguities found after the validated inline-hotkey polish:

1. Let users choose whether DeskRealm starts directly in the notification area or shows Realm Studio.
2. Make realm lock icons represent the **current state**, never an implied future action.

## Audit conclusions

- Current launches always call `HideToTray()` after the first-run import gate, so the user cannot keep Realm Studio visible at startup.
- Existing installed configurations must preserve the current behavior; the migration default must therefore be `startMinimized = true`.
- The lock button currently displays the next action (`🔓` while the card says `Realm locked`). That is technically valid as a command glyph but visually ambiguous beside state text.
- The default-realm star already represents current state (`⭐` / `🌟`), so realm lock should follow the same state-first pattern.

## Implementation checklist

- [x] Add GUID-independent `startMinimized` config setting with default `true`.
- [x] Migrate existing schema v17 configurations to v18 without changing their launch behavior.
- [x] Thread the setting through snapshot → view model → Automation toggle → global settings save.
- [x] Make application startup show or hide Realm Studio according to the saved setting after normal runtime initialization.
- [x] Preserve first-run import visibility regardless of the startup-visibility preference.
- [x] Change lock glyph semantics: closed lock = currently locked; open lock = currently unlocked.
- [x] Make lock tooltips explicitly state both current state and click result.
- [x] Update README, CHANGELOG, configuration, installation, architecture, safety, technical audit, release notes, patch notes, smoke test, journal and active TODO.
- [x] Perform static validation and package one clean-root ZIP.

## Explicit non-goals

- No change to Start with Windows registry registration.
- No background fallback or hidden process behavior beyond the saved user setting.
- No change to inherited realm/variant lock semantics.
- No dependency/platform upgrade within this UX patch.
