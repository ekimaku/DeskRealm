# PATCH NOTES - v0.5.1 release helper fix3

## Scope

Local maintainer tooling only. `.local-tools/` remains ignored by Git and is not part of the public DeskRealm source release.

## Fixes

- Renamed the helper internal remaining-arguments parameter from `$Arguments` to `$CommandArgs`.
- This avoids PowerShell interpreting `git add -A` as the abbreviated `-Arguments` parameter.
- Cleaned release helper step output so blank lines render as real blank lines instead of literal `` `n`` text.

## Validation

Static validation checks passed:

- no fragile PowerShell here-strings
- no escaped quote sequences
- no `$Arguments` parameter collision
- `.local-tools/` remains ignored by `.gitignore`
