# TODO — v0.5.9 clean buttons + display breakdown

## Block scope

Polish the modern v0.5.9 UI after Windows testing showed residual native-button paint artifacts and confusing combined display-topology summaries in the Icon Layout tree.

## Tasks

- [x] Audit current WinForms custom button implementation.
- [x] Replace the inherited native `Button` rendering path with a pure owner-painted `Control`-based `ModernButton`.
- [x] Keep the existing click/hover/pressed/disabled behavior without relying on classic WinForms button chrome.
- [x] Preserve navigation selected state and action button usage.
- [x] Extend icon-layout variant snapshots with per-display working-area metadata.
- [x] Render each variant display separately using `workingWidth` + `workingHeight` from the persisted display topology screens.
- [x] Mark the primary display with `✅` in the Icon Layout variant row.
- [x] Keep realm locks and variant locks behavior unchanged.
- [x] Update README / CHANGELOG / release notes / patch notes / architecture / safety / technical audit / smoke test.
- [x] Package a clean ZIP with `DeskRealm/` root folder.

## Validation notes

- Static checks confirm there are no remaining `Button` inheritance or `UseVisualStyleBackColor` references for `ModernButton`.
- Build must still be validated on Windows with `./scripts/Build-Release.ps1` because the sandbox does not include the .NET SDK.
