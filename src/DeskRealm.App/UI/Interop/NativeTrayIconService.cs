// DeskRealm-RealmStudio-Schema: v0.7.0
using DeskRealm.App.Services;
using System.Runtime.InteropServices;

namespace DeskRealm.App.Interop;

/// <summary>
/// Documented Shell_NotifyIcon integration for the WinUI window. It deliberately
/// avoids a WinForms tray dependency and remains isolated from Realm operations.
/// </summary>
internal sealed class NativeTrayIconService : IDisposable
{
    private const uint WM_NULL = 0x0000;
    private const uint WM_TIMER = 0x0113;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const uint WM_USER = 0x0400;
    private const uint NIN_SELECT = WM_USER;
    private const uint NIN_KEYSELECT = WM_USER + 1;
    private const uint WM_APP = 0x8000;
    private const uint TrayCallbackMessage = WM_APP + 0x2B;
    private static readonly nuint TrayLeftClickTimerId = (nuint)(TrayCallbackMessage + 1);
    private const uint FallbackDoubleClickDelayMilliseconds = 500;
    private const string TaskbarCreatedMessageName = "TaskbarCreated";

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const uint NIF_GUID = 0x00000020;
    private const uint NOTIFYICON_VERSION_4 = 4;

    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint MF_STRING = 0x0000;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint LR_DEFAULTSIZE = 0x0040;
    private const uint IMAGE_ICON = 1;
    private const int SW_RESTORE = 9;
    private const int SW_HIDE = 0;

    private const uint CommandOpen = 1;
    private const uint CommandRefreshNow = 2;
    private const uint CommandSyncNames = 3;
    private const uint CommandSaveLayout = 4;
    private const uint CommandRestoreLayout = 5;
    private const uint CommandReloadHotkeys = 6;
    private const uint CommandTogglePause = 7;
    private const uint CommandToggleStartup = 8;
    private const uint CommandOpenRealms = 9;
    private const uint CommandOpenConfig = 10;
    private const uint CommandOpenLogs = 11;
    private const uint CommandRestoreOriginalDesktop = 12;
    private const uint CommandQuit = 13;

    private static readonly Guid TrayGuid = Guid.Parse("fc9535b5-9d12-4f65-942c-1c1d1e9b4ad9");
    private static readonly SUBCLASSPROC SubclassProc = WindowSubclassProcedure;

    private readonly FileLogger _logger;
    private readonly NativeTrayMenuActions _actions;
    private GCHandle _selfHandle;
    private nint _window;
    private nint _iconHandle;
    private uint _taskbarCreatedMessage;
    private bool _visible;
    private bool _leftClickMenuPending;
    private bool _disposed;

    public event Action? DisplayTopologyChanged;

    public NativeTrayIconService(FileLogger logger, NativeTrayMenuActions actions)
    {
        _logger = logger;
        _actions = actions;
    }

    public void Initialize(nint windowHandle)
    {
        if (_visible) throw new InvalidOperationException("Native tray service was already initialized.");
        if (windowHandle == IntPtr.Zero) throw new InvalidOperationException("Cannot attach a tray icon to an empty WinUI window handle.");

        _window = windowHandle;
        _selfHandle = GCHandle.Alloc(this);
        if (!SetWindowSubclass(windowHandle, SubclassProc, (nuint)TrayCallbackMessage, GCHandle.ToIntPtr(_selfHandle)))
        {
            _selfHandle.Free();
            throw new InvalidOperationException($"Could not attach DeskRealm tray message handler. Win32Error={Marshal.GetLastWin32Error()}.");
        }

        _taskbarCreatedMessage = RegisterWindowMessageW(TaskbarCreatedMessageName);
        if (_taskbarCreatedMessage == 0)
        {
            CleanupSubclass();
            throw new InvalidOperationException($"Could not register the '{TaskbarCreatedMessageName}' shell message. Win32Error={Marshal.GetLastWin32Error()}.");
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DeskRealm.ico");
        _iconHandle = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        if (_iconHandle == IntPtr.Zero)
        {
            CleanupSubclass();
            throw new InvalidOperationException($"DeskRealm tray icon asset could not be loaded from '{iconPath}'. Win32Error={Marshal.GetLastWin32Error()}.");
        }

        if (!TryRegisterTrayIcon("initialized", out var error))
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
            CleanupSubclass();
            throw new InvalidOperationException($"Could not add DeskRealm to the Windows notification area. Win32Error={error}.");
        }

        _visible = true;
    }

    public void ShowInfo(string title, string text, bool warning = false)
    {
        if (!_visible) return;
        var data = BuildData(NIF_INFO | NIF_GUID);
        data.szInfo = Trim(text, 255);
        data.szInfoTitle = Trim(title, 63);
        data.dwInfoFlags = warning ? 2u : 1u;
        if (!Shell_NotifyIconW(NIM_MODIFY, ref data)) _logger.Warn($"Tray notification could not be shown. Win32Error={Marshal.GetLastWin32Error()}");
    }

    public static void ShowWindow(nint window)
    {
        _ = ShowWindowCore(window, SW_RESTORE);
        _ = SetForegroundWindow(window);
    }

    public static void HideWindow(nint window) => _ = ShowWindowCore(window, SW_HIDE);

    private NOTIFYICONDATAW BuildData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = TrayCallbackMessage,
        hIcon = _iconHandle,
        szTip = "DeskRealm",
        guidItem = TrayGuid
    };

    private static nint WindowSubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint subclassId, nint referenceData)
    {
        NativeTrayIconService? instance = null;
        if (referenceData != IntPtr.Zero)
        {
            try { instance = GCHandle.FromIntPtr(referenceData).Target as NativeTrayIconService; } catch { /* teardown race */ }
        }

        if (instance is not null)
        {
            if (message == instance._taskbarCreatedMessage)
            {
                instance.RestoreTrayIconAfterExplorerRestart();
                return IntPtr.Zero;
            }

            if (message == TrayCallbackMessage)
            {
                // NOTIFYICON_VERSION_4 stores the notification code in LOWORD(lParam).
                instance.HandleTrayMessage(LowWord(lParam));
                return IntPtr.Zero;
            }

            if (message == WM_TIMER && wParam == TrayLeftClickTimerId)
            {
                instance.ShowPendingLeftClickMenu();
                return IntPtr.Zero;
            }

            if (message == WM_DISPLAYCHANGE)
            {
                instance.NotifyDisplayTopologyChanged();
            }
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private static uint LowWord(nint value) => unchecked((uint)((nuint)value & 0xFFFFu));

    private bool TryRegisterTrayIcon(string reason, out int error)
    {
        var data = BuildData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_GUID);
        if (!Shell_NotifyIconW(NIM_ADD, ref data))
        {
            error = Marshal.GetLastWin32Error();
            _logger.Error($"Native DeskRealm tray icon registration failed: reason={reason}, Win32Error={error}.");
            return false;
        }

        data.uVersion = NOTIFYICON_VERSION_4;
        if (!Shell_NotifyIconW(NIM_SETVERSION, ref data))
        {
            _logger.Warn($"DeskRealm tray icon version negotiation failed: reason={reason}, Win32Error={Marshal.GetLastWin32Error()}.");
        }

        error = 0;
        _logger.Info($"Native DeskRealm tray icon {reason} (NOTIFYICON_VERSION_4 callback decoding enabled).");
        return true;
    }

    private void RestoreTrayIconAfterExplorerRestart()
    {
        if (_disposed || !_visible) return;

        CancelPendingLeftClickMenu();
        if (TryRegisterTrayIcon("re-registered after shell taskbar creation", out var error))
        {
            _logger.Info("Native DeskRealm tray icon restored after shell taskbar recreation (TaskbarCreated).");
            return;
        }

        _logger.Error($"Native DeskRealm tray icon could not be restored after shell taskbar recreation. DeskRealm remains running, but the tray entry is unavailable until DeskRealm is restarted. Win32Error={error}.");
    }

    private void NotifyDisplayTopologyChanged()
    {
        if (_disposed) return;
        _logger.Info("Native display topology notification received (WM_DISPLAYCHANGE).");
        DisplayTopologyChanged?.Invoke();
    }

    private void HandleTrayMessage(uint notification)
    {
        if (_disposed) return;

        switch (notification)
        {
            case WM_LBUTTONUP:
            case NIN_SELECT:
                QueueLeftClickMenu();
                break;

            case WM_LBUTTONDBLCLK:
            case NIN_KEYSELECT:
                CancelPendingLeftClickMenu();
                _logger.Info("Native tray activation received: opening Realm Studio.");
                InvokeAction("Open Realm Studio", _actions.OpenRealmStudio);
                break;

            case WM_RBUTTONUP:
            case WM_CONTEXTMENU:
                CancelPendingLeftClickMenu();
                ShowContextMenu();
                break;
        }
    }

    private void QueueLeftClickMenu()
    {
        _leftClickMenuPending = true;
        _ = KillTimer(_window, TrayLeftClickTimerId);
        if (SetTimer(_window, TrayLeftClickTimerId, GetTrayClickDelayMilliseconds(), IntPtr.Zero) == 0)
        {
            _leftClickMenuPending = false;
            _logger.Warn($"Native tray left-click menu timer could not be armed. Win32Error={Marshal.GetLastWin32Error()}");
            ShowContextMenu();
            return;
        }

        _logger.Info("Native tray left-click received: waiting for the system double-click interval before opening the menu.");
    }

    private static uint GetTrayClickDelayMilliseconds()
    {
        var systemInterval = GetDoubleClickTime();
        return systemInterval == 0 ? FallbackDoubleClickDelayMilliseconds : systemInterval;
    }

    private void ShowPendingLeftClickMenu()
    {
        _ = KillTimer(_window, TrayLeftClickTimerId);
        if (!_leftClickMenuPending || _disposed) return;
        _leftClickMenuPending = false;
        _logger.Info("Native tray left-click confirmed: opening menu.");
        ShowContextMenu();
    }

    private void CancelPendingLeftClickMenu()
    {
        if (!_leftClickMenuPending) return;
        _leftClickMenuPending = false;
        _ = KillTimer(_window, TrayLeftClickTimerId);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            _logger.Warn($"Native tray context menu creation failed. Win32Error={Marshal.GetLastWin32Error()}");
            return;
        }

        try
        {
            AppendMenuW(menu, MF_STRING, CommandOpen, "Open Realm Studio");
            AppendMenuW(menu, MF_STRING, CommandRefreshNow, "Refresh realm now");
            AppendMenuW(menu, MF_STRING, CommandSyncNames, "Sync realm names");
            AppendMenuW(menu, MF_STRING, CommandSaveLayout, "Save current icon layout");
            AppendMenuW(menu, MF_STRING, CommandRestoreLayout, "Restore current icon layout");
            AppendMenuW(menu, MF_STRING, CommandReloadHotkeys, "Reload realm hotkeys");
            AppendMenuW(menu, MF_STRING, CommandTogglePause, "Pause / resume automation");
            var startupFlags = MF_STRING | (SafeGetStartupEnabled() ? MF_CHECKED : 0u);
            AppendMenuW(menu, startupFlags, CommandToggleStartup, "Start with Windows");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, CommandOpenRealms, "Open realms folder");
            AppendMenuW(menu, MF_STRING, CommandOpenConfig, "Open configuration");
            AppendMenuW(menu, MF_STRING, CommandOpenLogs, "Open logs");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, CommandRestoreOriginalDesktop, "Restore original Desktop");
            AppendMenuW(menu, MF_STRING, CommandQuit, "Quit DeskRealm");

            if (!GetCursorPos(out var point))
            {
                _logger.Warn($"Native tray context menu cursor lookup failed. Win32Error={Marshal.GetLastWin32Error()}");
                return;
            }

            _ = SetForegroundWindow(_window);
            var command = TrackPopupMenu(menu, TPM_NONOTIFY | TPM_RETURNCMD | TPM_RIGHTBUTTON, point.X, point.Y, 0, _window, IntPtr.Zero);
            ExecuteMenuCommand(command);
        }
        finally
        {
            _ = DestroyMenu(menu);
            // Required notification-area menu cleanup: it prevents the second popup from immediately disappearing.
            _ = PostMessageW(_window, WM_NULL, 0, IntPtr.Zero);
        }
    }

    private bool SafeGetStartupEnabled()
    {
        try { return _actions.IsStartWithWindowsEnabled(); }
        catch (Exception ex)
        {
            _logger.Error("Could not read Start with Windows state for the native tray menu.", ex);
            return false;
        }
    }

    private void ExecuteMenuCommand(uint command)
    {
        switch (command)
        {
            case CommandOpen: InvokeAction("Open Realm Studio", _actions.OpenRealmStudio); break;
            case CommandRefreshNow: InvokeAction("Refresh realm now", _actions.RefreshNow); break;
            case CommandSyncNames: InvokeAction("Sync realm names", _actions.SyncRealmNames); break;
            case CommandSaveLayout: InvokeAction("Save current icon layout", _actions.SaveIconLayout); break;
            case CommandRestoreLayout: InvokeAction("Restore current icon layout", _actions.RestoreIconLayout); break;
            case CommandReloadHotkeys: InvokeAction("Reload realm hotkeys", _actions.ReloadHotkeys); break;
            case CommandTogglePause: InvokeAction("Pause / resume automation", _actions.ToggleAutomation); break;
            case CommandToggleStartup: InvokeAction("Toggle Start with Windows", _actions.ToggleStartWithWindows); break;
            case CommandOpenRealms: InvokeAction("Open realms folder", _actions.OpenRealmsFolder); break;
            case CommandOpenConfig: InvokeAction("Open configuration", _actions.OpenConfiguration); break;
            case CommandOpenLogs: InvokeAction("Open logs", _actions.OpenLogs); break;
            case CommandRestoreOriginalDesktop: InvokeAction("Restore original Desktop", _actions.RestoreOriginalDesktop); break;
            case CommandQuit: InvokeAction("Quit DeskRealm", _actions.Quit); break;
        }
    }

    private void InvokeAction(string actionName, Action action)
    {
        try
        {
            _logger.Info($"Native tray action invoked: {actionName}.");
            action();
        }
        catch (Exception ex)
        {
            _logger.Error($"Native tray action failed: {actionName}.", ex);
            ShowInfo("DeskRealm — tray action failed", ex.Message, warning: true);
        }
    }

    private void CleanupSubclass()
    {
        CancelPendingLeftClickMenu();
        if (_window != IntPtr.Zero)
        {
            _ = RemoveWindowSubclass(_window, SubclassProc, (nuint)TrayCallbackMessage);
        }
        if (_selfHandle.IsAllocated) _selfHandle.Free();
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_visible)
        {
            var data = BuildData(NIF_GUID);
            _ = Shell_NotifyIconW(NIM_DELETE, ref data);
            _visible = false;
        }
        if (_iconHandle != IntPtr.Zero)
        {
            _ = DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
        CleanupSubclass();
        _disposed = true;
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max];

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize; public nint hWnd; public uint uID; public uint uFlags; public uint uCallbackMessage; public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState; public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private delegate nint SUBCLASSPROC(nint hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nint dwRefData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadImageW(nint instance, string name, uint type, int cx, int cy, uint fuLoad);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyIcon(nint hIcon);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint hWnd, SUBCLASSPROC callback, nuint subclassId, nint refData);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool RemoveWindowSubclass(nint hWnd, SUBCLASSPROC callback, nuint subclassId);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hWnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool AppendMenuW(nint menu, uint flags, uint item, string? text);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hWnd, nint rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll", SetLastError = true)] private static extern nuint SetTimer(nint hWnd, nuint timerId, uint elapsed, nint callback);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool KillTimer(nint hWnd, nuint timerId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterWindowMessageW(string message);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessageW(nint hWnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "ShowWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowCore(nint hWnd, int command);
}

/// <summary>
/// Single action contract between the native notification-area adapter and the
/// serialized Realm Studio runtime. The adapter owns only routing and menus;
/// it never performs desktop mutations on its own.
/// </summary>
internal sealed record NativeTrayMenuActions(
    Action OpenRealmStudio,
    Action RefreshNow,
    Action SyncRealmNames,
    Action SaveIconLayout,
    Action RestoreIconLayout,
    Action ReloadHotkeys,
    Action ToggleAutomation,
    Func<bool> IsStartWithWindowsEnabled,
    Action ToggleStartWithWindows,
    Action OpenRealmsFolder,
    Action OpenConfiguration,
    Action OpenLogs,
    Action RestoreOriginalDesktop,
    Action Quit);
