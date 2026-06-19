# Contributing to DeskRealm

Thanks for wanting to help.

DeskRealm touches sensitive Windows Shell behavior, so contributions should stay conservative, explicit, and easy to troubleshoot.

## Project rules

- No silent fallback.
- No hidden file migration.
- No automatic folder merge.
- No telemetry.
- No network feature unless explicitly discussed and documented.
- Preserve emergency restore behavior.
- Log errors clearly.
- Keep risky Shell/COM icon operations isolated from the main tray process.

## Development setup

Requirements:

- Windows 10/11
- .NET 10 SDK

Build:

```powershell
.\scripts\Build-Release.ps1
```

Run from source:

```powershell
.\scripts\Run-DeskRealm.ps1
```

## Pull requests

Before opening a PR:

1. Explain the user-facing behavior change.
2. Document any new registry/API usage.
3. Update README/docs when behavior changes.
4. Add or update patch notes / changelog.
5. Confirm the emergency restore flow still works.
6. Avoid adding dependencies unless there is a clear reason.

## Contribution license

Unless explicitly stated otherwise, contributions are submitted under the Apache License 2.0, the same license as DeskRealm.
