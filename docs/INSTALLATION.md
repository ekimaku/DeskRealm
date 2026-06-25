# Installation and local development

## Public users

Download the latest **v0.7.0** release asset from GitHub Releases.

### Portable ZIP

1. Extract the ZIP into a fresh folder.
2. Run `DeskRealm.App.exe`.
3. Use the notification-area icon to open Realm Studio or quit DeskRealm.

### Install bundle

1. Extract the install-bundle ZIP.
2. Review `Install-DeskRealm.ps1`.
3. Open PowerShell in that folder and run the script.

## Source development

```powershell
.\scripts\Run-DeskRealm.ps1
```

For a portable local build:

```powershell
.\scripts\Build-Release.ps1
.\dist\DeskRealm\DeskRealm.App.exe
```

## Requirements

- Windows 10 version 2004+ or Windows 11
- .NET SDK selected by `global.json`
- Windows App SDK dependencies restored through NuGet
- PowerShell for repository scripts

## Upgrade notes

Before upgrading from an older DeskRealm build, copy `%APPDATA%\DeskRealm\deskrealm.config.json` if you want a rollback snapshot.

v0.7.0 converts legacy number-keyed `desktopHotkeys` to GUID-keyed realm hotkeys once live Windows desktop metadata is available. This is a one-time migration; it does not move Desktop files.

## Troubleshooting

- **`dotnet SDK was not found`**: install the SDK selected in `global.json`.
- **Publish error involving PRI/MSIX tooling**: use `Build-Release.ps1`; the project enables the required Windows App SDK tooling only for single-file publish.
- **Tray icon missing after Explorer restart**: restart DeskRealm from Diagnostics, then inspect `%LOCALAPPDATA%\DeskRealm\logs\deskrealm.log` if it still does not return.
- **Wallpaper preview unavailable**: Windows exposed a missing or unreadable wallpaper path. DeskRealm leaves Windows unchanged; select a new image through the card or editor to create a managed preview.
- **OneDrive Desktop backup enabled**: read [Safety and privacy](SAFETY_AND_PRIVACY.md) before enabling automation.
