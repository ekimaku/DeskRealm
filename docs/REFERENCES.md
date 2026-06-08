# References and technical sources

DeskRealm does not bundle third-party source code. The following references informed design and implementation.

## Microsoft / Windows documentation

- Known Folders overview: https://learn.microsoft.com/en-us/previous-versions/windows/desktop/legacy/bb776911(v=vs.85)
- `SHSetKnownFolderPath`: https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shsetknownfolderpath
- `IVirtualDesktopManager`: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager
- Windows keyboard shortcuts / virtual desktops: https://support.microsoft.com/en-us/windows/keyboard-shortcuts-in-windows-dcc61a57-8ff0-cffe-9796-cb9706c75eec
- `IFolderView`: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview
- `IFolderView::GetItemPosition`: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-getitemposition
- `RegisterHotKey`: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey
- `WM_HOTKEY`: https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-hotkey
- `SendInput`: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
- Run and RunOnce registry keys: https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys

## Shell icon positioning

- Raymond Chen, The Old New Thing — “Manipulating the positions of desktop icons”: https://devblogs.microsoft.com/oldnewthing/20130318-00/?p=4933
- Raymond Chen, The Old New Thing — supported access to desktop icon positions: https://devblogs.microsoft.com/oldnewthing/20211122-00/?p=105948

## Virtual desktop registry exploration / related work

- Gérald Barré / Meziantou — “Listing Windows Virtual Desktops using .NET”: https://www.meziantou.net/listing-windows-virtual-desktops-using-dotnet.htm
- pmb6tz/windows-desktop-switcher: https://github.com/pmb6tz/windows-desktop-switcher
- Ciantic/VirtualDesktopAccessor: https://github.com/Ciantic/VirtualDesktopAccessor

## Licensing and citation

- Apache License 2.0: https://www.apache.org/licenses/LICENSE-2.0
- SPDX Apache-2.0 reference: https://spdx.org/licenses/Apache-2.0.html
- GitHub citation files: https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-citation-files
- Citation File Format: https://citation-file-format.github.io/
