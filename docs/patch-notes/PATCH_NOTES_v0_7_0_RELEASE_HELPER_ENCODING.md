# DeskRealm v0.7.0 — Release helper encoding repair

## Fixed

- Repaired `Prepare-GitHubRelease.ps1` for Windows PowerShell 5.1. A raw UTF-8 em dash in the script could be decoded as a smart quote when the file was parsed without a BOM, breaking the helper before it could run.
- The helper still requires the public CHANGELOG title format `## v0.7.0 — Release title`, but it now creates the em dash at runtime from Unicode code point `0x2014`.
- All executable PowerShell sources are now intentionally ASCII-only.

## User impact

`./scripts/Prepare-GitHubRelease.ps1 -Version 0.7.0` can run in Windows PowerShell 5.1 without the parser error caused by the release-title validation string.
