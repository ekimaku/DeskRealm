# Third-party notices

DeskRealm is Apache-2.0 licensed. Its self-contained WinUI 3 publish includes framework dependencies required by the application.

## Framework dependencies

- **Microsoft.WindowsAppSDK** `2.2.0` — Windows App SDK components used by the unpackaged WinUI 3 application.
- **CommunityToolkit.Mvvm** `8.4.2` — MVVM observable-property and command primitives used by DeskRealm view models.

Before a public release, inspect the resolved Windows `dist\DeskRealm` output and package license/notice requirements. A single-file executable does not remove redistribution obligations.

## References and prior art

DeskRealm uses Windows Known Folders, Shell folder views, global hotkeys, synthetic keyboard input, notification-area integration and HKCU Run startup. See `docs/REFERENCES.md`.

The virtual-desktop registry discovery approach was informed by Gérald Barré / Meziantou. DeskRealm does not bundle VirtualDesktopAccessor or code from the other reference projects listed in `docs/REFERENCES.md`.
