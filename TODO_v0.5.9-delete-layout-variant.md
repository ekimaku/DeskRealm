# TODO — v0.5.9 delete layout variant

## Scope

Add an explicit destructive action in the **Icon Layout** tab so users can remove stale saved display-topology variants from a desktop layout.

## Audit

- The previous row displayed a passive `SAVED` pill.
- Saved variants are persisted inside `%APPDATA%\\DeskRealm\\icon-layouts\\<desktop-guid>.json` under the `variants` array.
- Variant locks are stored separately in `lockedIconLayoutVariants` and must be cleaned when the variant is removed.
- A realm/layout parent lock must continue to gray out and disable child row actions.

## Implementation

- [x] Replace the `SAVED` pill with a `Delete` action when the row has a persisted layout variant.
- [x] Keep `EMPTY` as a passive pill for unsaved/pending variants.
- [x] Disable variant deletion when the parent realm or desktop layout lock is inherited.
- [x] Add confirmation before deleting a saved variant.
- [x] Make the confirmation explicit that Desktop files/icons are not deleted.
- [x] Delete only the selected saved variant from the layout JSON.
- [x] Remove the associated `lockedIconLayoutVariants` entry.
- [x] If the last variant is removed, delete the now-empty layout JSON file.
- [x] If variants remain, promote the newest remaining variant into the legacy/current top-level layout fields.
- [x] Refresh Icon Layout, lock state and Status after deletion.

## Validation

- Static check: delete action routes through UI confirmation.
- Static check: no silent fallback; missing file/variant raises an explicit error.
- Static check: parent realm/layout locks still disable child variant destructive actions.
- Build must be validated on Windows with `scripts\\Build-Release.ps1`.
