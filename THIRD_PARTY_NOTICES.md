# Third-party notices

DeskRealm currently does **not** include or redistribute third-party source code, DLLs, NuGet packages, or external binaries beyond the .NET / Windows Desktop runtime APIs used by the application.

## Referenced documentation and prior art

The following projects/pages informed implementation choices and are credited as references. No source code from these projects is copied or bundled in DeskRealm.

### Microsoft / Win32 documentation

DeskRealm uses Windows APIs such as Known Folders, Shell folder views, global hotkeys, synthetic keyboard input, and HKCU Run startup registry entries. See `docs/REFERENCES.md`.

### Raymond Chen / The Old New Thing

DeskRealm's Desktop icon positioning approach follows the documented/supported `IFolderView` direction described by Raymond Chen, especially `IFolderView::SelectAndPositionItems`.

### Gérald Barré / Meziantou

The virtual desktop registry discovery approach was informed by Gérald Barré's article about listing Windows virtual desktops using .NET.

### pmb6tz/windows-desktop-switcher

DeskRealm's direct-numbered virtual desktop hotkey feature is related to the broader idea of keyboard-based virtual desktop switching as explored by pmb6tz/windows-desktop-switcher. DeskRealm does not copy or bundle code from that project.

### Ciantic/VirtualDesktopAccessor

DeskRealm intentionally does not bundle VirtualDesktopAccessor.dll, but the project is relevant prior art for Windows virtual desktop tooling.

## License compatibility note

Because no third-party code is bundled, there are currently no third-party license texts that must be redistributed with DeskRealm beyond DeskRealm's own Apache-2.0 license.
