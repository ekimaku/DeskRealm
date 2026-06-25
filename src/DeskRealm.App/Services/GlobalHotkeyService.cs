// DeskRealm-RealmStudio-Schema: v0.7.0
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace DeskRealm.App.Services;

/// <summary>
/// Owns a message-only HWND used by RegisterHotKey. Windows requires every
/// RegisterHotKey / UnregisterHotKey call to happen on the thread that owns that HWND.
/// Realm Studio operations run through a serialized worker lane, therefore this service
/// posts owner-thread work to the HWND itself and lets its WndProc execute it explicitly.
/// </summary>
internal sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    // This private WM_APP message is posted to the same message-only HWND that owns the
    // RegisterHotKey registration. Its WndProc therefore executes on the correct native
    // owner thread even when Realm Studio requested the operation from its worker lane.
    private const uint WM_DESKREALM_OWNER_DISPATCH = 0x8000 + 0x3A;
    private const int OwnerDispatchTimeoutMs = 5000;

    private readonly FileLogger _logger;
    private readonly uint _ownerThreadId;
    private readonly HotkeyMessageWindow _window;
    private readonly ConcurrentQueue<OwnerThreadWorkItem> _ownerThreadWork = new();
    private readonly Dictionary<int, RegisteredRealmHotkey> _registered = new();
    private int _nextId = 4100;
    private int _registeredCount;
    private bool _disposed;
    private IReadOnlyList<string> _lastRegistrationErrors = Array.Empty<string>();

    public event Action<Guid, string>? RealmHotkeyPressed;

    public int RegisteredCount => Volatile.Read(ref _registeredCount);
    public IReadOnlyList<string> LastRegistrationErrors => _lastRegistrationErrors;

    public GlobalHotkeyService(FileLogger logger)
    {
        _logger = logger;
        _window = new HotkeyMessageWindow(this);
        _ownerThreadId = _window.OwnerThreadId;
        _logger.Info($"Native realm hotkey message window created on thread {_ownerThreadId}; owner-thread work is dispatched through its WndProc.");
    }

    public IReadOnlyList<string> Start(RealmConfig config)
        => InvokeOnOwnerThread("register realm hotkeys", () => StartCore(config));

    public void Stop()
        => InvokeOnOwnerThread("unregister realm hotkeys", StopCore);

    private IReadOnlyList<string> StartCore(RealmConfig config)
    {
        EnsureOwnerThread("RegisterHotKey");
        StopCore();
        if (!config.DesktopHotkeysEnabled)
        {
            _logger.Info("Realm hotkeys disabled by config.");
            _lastRegistrationErrors = Array.Empty<string>();
            return _lastRegistrationErrors;
        }

        var errors = new List<string>();
        foreach (var pair in config.RealmHotkeys.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(pair.Key, out var desktopId))
            {
                errors.Add($"Invalid realmHotkeys GUID key: {pair.Key}");
                continue;
            }

            HotkeyBinding binding;
            try
            {
                binding = HotkeyParser.Parse(desktopId, pair.Value);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                continue;
            }

            var id = _nextId++;
            if (!RegisterHotKey(_window.Handle, id, binding.Modifiers, binding.VirtualKey))
            {
                errors.Add($"Hotkey rejected by Windows: {binding.Text} -> realm {desktopId:B}. Win32Error={Marshal.GetLastWin32Error()}.");
                continue;
            }

            _registered[id] = new RegisteredRealmHotkey(desktopId, binding);
            _logger.Info($"Realm hotkey registered: {binding.Text} -> {desktopId:B}");
        }

        if (_registered.Count == 0 && config.DesktopHotkeysEnabled && config.RealmHotkeys.Count > 0)
        {
            errors.Add("No DeskRealm realm hotkey was registered.");
        }

        _lastRegistrationErrors = errors.ToArray();
        Volatile.Write(ref _registeredCount, _registered.Count);
        foreach (var error in errors) _logger.Warn("Hotkey registration issue: " + error);
        _logger.Info($"Realm hotkey registration summary: registered={_registered.Count}, configured={config.RealmHotkeys.Count}, errors={_lastRegistrationErrors.Count}.");
        return _lastRegistrationErrors;
    }

    private void StopCore()
    {
        EnsureOwnerThread("UnregisterHotKey");
        foreach (var id in _registered.Keys.ToList())
        {
            if (!UnregisterHotKey(_window.Handle, id))
            {
                _logger.Warn($"Hotkey unregister failed: id={id}, Win32Error={Marshal.GetLastWin32Error()}");
            }
        }
        if (_registered.Count > 0) _logger.Info($"Realm hotkeys unregistered: {_registered.Count}");
        _registered.Clear();
        Volatile.Write(ref _registeredCount, 0);
        _lastRegistrationErrors = Array.Empty<string>();
    }

    private T InvokeOnOwnerThread<T>(string operation, Func<T> action)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GlobalHotkeyService));
        if (GetCurrentThreadId() == _ownerThreadId) return action();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new OwnerThreadWorkItem(operation, () =>
        {
            try { completion.TrySetResult(action()); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        _ownerThreadWork.Enqueue(work);

        if (!PostMessageW(_window.Handle, WM_DESKREALM_OWNER_DISPATCH, UIntPtr.Zero, IntPtr.Zero))
        {
            work.Cancel();
            throw new InvalidOperationException($"DeskRealm could not post '{operation}' to the native hotkey HWND owner thread. Win32Error={Marshal.GetLastWin32Error()}.");
        }

        try
        {
            return completion.Task.WaitAsync(TimeSpan.FromMilliseconds(OwnerDispatchTimeoutMs)).GetAwaiter().GetResult();
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"DeskRealm timed out waiting for the native hotkey HWND owner thread while trying to {operation}.", ex);
        }
    }

    private void InvokeOnOwnerThread(string operation, Action action)
        => InvokeOnOwnerThread(operation, () => { action(); return true; });

    private void ExecuteOwnerThreadDispatch()
    {
        EnsureOwnerThread("owner-thread dispatch");
        while (_ownerThreadWork.TryDequeue(out var work))
        {
            if (work.IsCancelled) continue;
            _logger.Info($"Native hotkey owner-thread dispatch: {work.Operation}.");
            work.Execute();
        }
    }

    private void EnsureOwnerThread(string api)
    {
        var actualOwnerThread = GetWindowThreadProcessId(_window.Handle, out _);
        var current = GetCurrentThreadId();
        if (actualOwnerThread == 0)
        {
            throw new InvalidOperationException($"{api} cannot validate the DeskRealm hotkey message window owner. Win32Error={Marshal.GetLastWin32Error()}.");
        }
        if (actualOwnerThread != _ownerThreadId || current != actualOwnerThread)
        {
            throw new InvalidOperationException($"{api} must execute on DeskRealm hotkey HWND owner thread {actualOwnerThread}, not thread {current}.");
        }
    }

    private void OnHotkeyMessage(int id)
    {
        if (!_registered.TryGetValue(id, out var registered))
        {
            _logger.Warn($"WM_HOTKEY received for an unknown id: {id}");
            return;
        }

        _logger.Info($"Realm hotkey pressed: {registered.Binding.Text} -> {registered.DesktopId:B}");
        RealmHotkeyPressed?.Invoke(registered.DesktopId, registered.Binding.Text);
    }

    public void Dispose()
    {
        if (_disposed) return;
        InvokeOnOwnerThread("dispose native realm hotkeys", () =>
        {
            StopCore();
            _window.Dispose();
            _disposed = true;
        });
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(nint hWnd, uint msg, UIntPtr wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private sealed record RegisteredRealmHotkey(Guid DesktopId, HotkeyBinding Binding);

    private sealed class OwnerThreadWorkItem
    {
        private readonly Action _execute;
        private int _cancelled;

        public OwnerThreadWorkItem(string operation, Action execute)
        {
            Operation = operation;
            _execute = execute;
        }

        public string Operation { get; }
        public bool IsCancelled => Volatile.Read(ref _cancelled) != 0;
        public void Cancel() => Interlocked.Exchange(ref _cancelled, 1);
        public void Execute() => _execute();
    }

    private sealed class HotkeyMessageWindow : IDisposable
    {
        private const nint HWND_MESSAGE = -3;
        private const uint WM_NCDESTROY = 0x0082;
        private static readonly WndProcDelegate WndProc = DispatchWindowMessage;
        private static readonly string WindowClassName = "DeskRealm.NativeHotkeyMessageWindow.v2";
        private static readonly object ClassLock = new();
        private static readonly Dictionary<nint, HotkeyMessageWindow> Instances = [];
        private static ushort _classAtom;

        private readonly GlobalHotkeyService _owner;
        private bool _disposed;

        public HotkeyMessageWindow(GlobalHotkeyService owner)
        {
            _owner = owner;
            EnsureClassRegistered();
            Handle = CreateWindowExW(0, WindowClassName, "DeskRealmHotkeys", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
            if (Handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Could not create the DeskRealm hotkey message window. Win32Error={Marshal.GetLastWin32Error()}.");
            }

            OwnerThreadId = GlobalHotkeyService.GetWindowThreadProcessId(Handle, out _);
            if (OwnerThreadId == 0)
            {
                throw new InvalidOperationException($"Could not identify the DeskRealm hotkey message window owner thread. Win32Error={Marshal.GetLastWin32Error()}.");
            }

            lock (Instances) Instances.Add(Handle, this);
        }

        public nint Handle { get; }
        public uint OwnerThreadId { get; }

        private static void EnsureClassRegistered()
        {
            lock (ClassLock)
            {
                if (_classAtom != 0) return;
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProc),
                    hInstance = GetModuleHandleW(null),
                    lpszClassName = WindowClassName
                };
                _classAtom = RegisterClassExW(ref wc);
                if (_classAtom == 0)
                {
                    throw new InvalidOperationException($"Could not register DeskRealm hotkey window class. Win32Error={Marshal.GetLastWin32Error()}.");
                }
            }
        }

        private static nint DispatchWindowMessage(nint hWnd, uint message, nuint wParam, nint lParam)
        {
            HotkeyMessageWindow? instance;
            lock (Instances) Instances.TryGetValue(hWnd, out instance);
            if (instance is not null && message == WM_HOTKEY)
            {
                instance._owner.OnHotkeyMessage(unchecked((int)wParam));
                return IntPtr.Zero;
            }

            if (instance is not null && message == GlobalHotkeyService.WM_DESKREALM_OWNER_DISPATCH)
            {
                instance._owner.ExecuteOwnerThreadDispatch();
                return IntPtr.Zero;
            }

            if (message == WM_NCDESTROY)
            {
                lock (Instances) Instances.Remove(hWnd);
            }
            return DefWindowProcW(hWnd, message, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (Handle != IntPtr.Zero && !DestroyWindow(Handle))
            {
                _owner._logger.Warn($"Hotkey message window destruction failed. Win32Error={Marshal.GetLastWin32Error()}");
            }
            _disposed = true;
        }

        private delegate nint WndProcDelegate(nint hWnd, uint message, nuint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEXW
        {
            public uint cbSize;
            public uint style;
            public nint lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public nint hInstance;
            public nint hIcon;
            public nint hCursor;
            public nint hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public nint hIconSm;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassExW([In] ref WNDCLASSEXW lpwcx);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(nint hWnd);
        [DllImport("user32.dll")]
        private static extern nint DefWindowProcW(nint hWnd, uint msg, nuint wParam, nint lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandleW(string? lpModuleName);
    }
}
