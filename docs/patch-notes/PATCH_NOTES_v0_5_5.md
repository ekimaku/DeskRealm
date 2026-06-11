# PATCH NOTES v0.5.5

## Goal

Reduce remaining icon placement misses after fast virtual desktop switches.

## Implementation

- Added chunked icon placement in `DesktopIconShellService`.
- Added post-restore verification using `IFolderView.GetItemPosition`.
- Added targeted retry for icons still outside the configured tolerance.
- Added final warning logs when an icon remains unresolved.

## Validation

Static validation only in the packaging environment. Local .NET build required on Windows.
