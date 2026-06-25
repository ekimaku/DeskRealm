# DeskRealm local release helper

This folder contains the generic, secret-free maintainer release helper. It is tracked so a fresh clone can follow the same GitHub publication route. Local tokens, GitHub CLI credentials and `.release-work` output are never committed.

```powershell
.\.local-tools\Publish-DeskRealmRelease.ps1 -Version 0.7.0 -DryRun
.\.local-tools\Publish-DeskRealmRelease.ps1 -Version 0.7.0
```

Before staging, the helper invokes `scripts\Prepare-GitHubRelease.ps1`. That validates version/release notes and runs the standard self-contained build. Use `-SkipLocalBuild` only for a deliberate maintainer exception.
