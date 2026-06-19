# DeskRealm installation — v0.6.0

## Normal users

Use a self-contained `win-x64` artifact from the GitHub Release. No separate .NET runtime is required.

### Portable

1. Extract `DeskRealm-0.6.0-win-x64-portable.zip` to a stable local path.
2. Run `DeskRealm.App.exe`.
3. Complete first-run onboarding before the first automatic realm switch.
4. Optionally enable **Start with Windows**.

### Install bundle

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-DeskRealm.ps1 -StartAfterInstall -StartWithWindows
```

Default destination:

```text
%LOCALAPPDATA%\Programs\DeskRealm
```

## Source build

Requirements:

- Windows 10/11 x64
- .NET 10 SDK compatible with `global.json`
- Windows Explorer as the Desktop shell

Run Debug:

```powershell
.\scripts\Run-DeskRealm.ps1
```

Build self-contained Release:

```powershell
.\scripts\Build-Release.ps1
```

## Upgrade from older DeskRealm builds

1. Quit the running DeskRealm process from its tray menu.
2. Keep `%APPDATA%\DeskRealm` and your realm folders.
3. Replace the application files with the v0.6.0 release.
4. Start DeskRealm and review the config migration log to schema `11`.
5. Validate each direct hotkey, one native `Win+Ctrl+Arrow` switch, manual save/restore and every monitor topology used.

`v0.6.0` supersedes earlier `0.5.x` builds and should be treated as the public baseline.

## Emergency restore

```powershell
.\scripts\Restore-Desktop.ps1
```

Do not delete realm folders as a recovery method. Restore the Known Folder first, then inspect files normally.

## Reboot recovery

At user logon, the first reconciliation restores the active realm once even if Windows already left the Desktop Known Folder on that realm. The layout recovery path does not require DeskRealm's explicit **Quit** action to have completed before the previous shutdown.
