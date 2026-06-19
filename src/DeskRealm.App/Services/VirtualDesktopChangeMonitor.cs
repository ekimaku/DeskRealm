using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DeskRealm.App.Services;

internal sealed class VirtualDesktopChangeMonitor : IDisposable
{
    private const string VirtualDesktopsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private const string SessionInfoKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo";

    private readonly RegistryKeyChangeSubscription _virtualDesktops;
    private readonly RegistryKeyChangeSubscription _sessionInfo;
    private readonly FileLogger _logger;
    private bool _disposed;

    public VirtualDesktopChangeMonitor(FileLogger logger)
    {
        _logger = logger;
        _virtualDesktops = new RegistryKeyChangeSubscription(VirtualDesktopsKey, watchSubtree: true, required: true, logger: logger);
        _sessionInfo = new RegistryKeyChangeSubscription(SessionInfoKey, watchSubtree: true, required: false, logger: logger);
        _virtualDesktops.Changed += OnChanged;
        _sessionInfo.Changed += OnChanged;
        _virtualDesktops.Faulted += OnFaulted;
        _sessionInfo.Faulted += OnFaulted;
    }

    public event Action<string>? Changed;
    public event Action<Exception>? Faulted;

    public void Start()
    {
        ThrowIfDisposed();
        _virtualDesktops.Start();
        _sessionInfo.Start();
        var sources = _sessionInfo.IsActive
            ? "Explorer\\VirtualDesktops + Explorer\\SessionInfo"
            : "Explorer\\VirtualDesktops";
        _logger.Info($"Virtual desktop registry notifications active: {sources}.");
    }

    private void OnChanged(string source)
    {
        if (!_disposed)
        {
            Changed?.Invoke(source);
        }
    }

    private void OnFaulted(Exception error)
    {
        if (!_disposed)
        {
            Faulted?.Invoke(error);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _virtualDesktops.Changed -= OnChanged;
        _sessionInfo.Changed -= OnChanged;
        _virtualDesktops.Faulted -= OnFaulted;
        _sessionInfo.Faulted -= OnFaulted;
        _virtualDesktops.Dispose();
        _sessionInfo.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class RegistryKeyChangeSubscription : IDisposable
    {
        private const uint RegNotifyChangeName = 0x00000001;
        private const uint RegNotifyChangeLastSet = 0x00000004;
        private const uint RegNotifyThreadAgnostic = 0x10000000;

        private readonly string _path;
        private readonly bool _watchSubtree;
        private readonly FileLogger _logger;
        private readonly bool _required;
        private readonly AutoResetEvent _signal = new(false);
        private RegistryKey? _key;
        private RegisteredWaitHandle? _waitHandle;
        private bool _started;
        private bool _disposed;

        public RegistryKeyChangeSubscription(string path, bool watchSubtree, bool required, FileLogger logger)
        {
            _path = path;
            _watchSubtree = watchSubtree;
            _required = required;
            _logger = logger;
        }

        public event Action<string>? Changed;
        public event Action<Exception>? Faulted;

        public bool IsActive => _waitHandle is not null;

        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _key = Registry.CurrentUser.OpenSubKey(_path, writable: false);
            if (_key is null)
            {
                if (_required)
                {
                    throw new InvalidOperationException($"Required registry notification source not found: HKCU\\{_path}");
                }

                _logger.Warn($"Optional registry notification source unavailable: HKCU\\{_path}. The primary VirtualDesktops observer remains active.");
                _started = true;
                return;
            }

            try
            {
                Arm();
                _waitHandle = ThreadPool.RegisterWaitForSingleObject(
                    _signal,
                    static (state, timedOut) => ((RegistryKeyChangeSubscription)state!).OnSignaled(timedOut),
                    this,
                    Timeout.Infinite,
                    executeOnlyOnce: false);
                _started = true;
                _logger.Info($"Registry notification armed: HKCU\\{_path} (subtree={_watchSubtree}).");
            }
            catch (Exception ex) when (!_required)
            {
                _waitHandle?.Unregister(null);
                _waitHandle = null;
                _key.Dispose();
                _key = null;
                _started = true;
                _logger.Warn($"Optional registry notification source could not be armed: HKCU\\{_path}. " +
                             $"The primary VirtualDesktops observer remains active. {ex.Message}");
            }
        }

        private void OnSignaled(bool timedOut)
        {
            if (timedOut || _disposed)
            {
                return;
            }

            try
            {
                Arm();
                Changed?.Invoke($"HKCU\\{_path}");
            }
            catch (Exception ex)
            {
                _waitHandle?.Unregister(null);
                _waitHandle = null;
                var role = _required ? "Primary" : "Optional";
                var failure = new InvalidOperationException(
                    $"{role} virtual desktop registry observer stopped: HKCU\\{_path}. Restart DeskRealm after reviewing the log.",
                    ex);
                _logger.Error($"Registry notification failed: HKCU\\{_path}", failure);
                Faulted?.Invoke(failure);
            }
        }

        private void Arm()
        {
            var key = _key ?? throw new InvalidOperationException($"Registry notification key is not open: HKCU\\{_path}");
            var result = RegNotifyChangeKeyValue(
                key.Handle,
                _watchSubtree,
                RegNotifyChangeName | RegNotifyChangeLastSet | RegNotifyThreadAgnostic,
                _signal.SafeWaitHandle,
                asynchronous: true);

            if (result != 0)
            {
                throw new Win32Exception(result, $"RegNotifyChangeKeyValue failed for HKCU\\{_path}.");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _waitHandle?.Unregister(null);
            _waitHandle = null;
            _key?.Dispose();
            _key = null;
            _signal.Dispose();
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegNotifyChangeKeyValue(
            SafeRegistryHandle hKey,
            [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
            uint dwNotifyFilter,
            SafeWaitHandle hEvent,
            [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
    }
}
