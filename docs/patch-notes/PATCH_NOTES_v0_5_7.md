# Patch notes — DeskRealm v0.5.7

## Focus

Add a safe first-run import flow for users who already have files and shortcuts on their normal Windows Desktop before starting DeskRealm.

## Changes

- Added `InitialDesktopImportForm`.
- Added config version `5` and import-related settings.
- Added first-run detection before the first automatic realm switch.
- Added strict initial Desktop import in `DesktopSwitchService`.
- Added optional layout save before moving Desktop items.
- Added conflict checks to prevent silent overwrite/merge.
- Updated README, configuration, installation, release process, smoke test and changelog for v0.5.7.

## Testing notes

- Test on a fresh config to see the wizard.
- Test with disposable Desktop files/shortcuts before moving real files.
- Confirm existing upgraded configs do not show the wizard automatically.
- Confirm imported layout is restored through the v0.5.6 Shell identity fallback engine.
