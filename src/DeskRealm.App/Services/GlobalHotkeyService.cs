using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskRealm.App.Services;

internal sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private readonly FileLogger _logger;
    private readonly HotkeyMessageWindow _window;
    private readonly Dictionary<int, HotkeyBinding> _registered = new();
    private int _nextId = 4100;
    private bool _disposed;

    public event Action<int, string>? DesktopHotkeyPressed;

    public GlobalHotkeyService(FileLogger logger)
    {
        _logger = logger;
        _window = new HotkeyMessageWindow(this);
    }

    public IReadOnlyList<string> Start(RealmConfig config)
    {
        Stop();

        if (!config.DesktopHotkeysEnabled)
        {
            _logger.Info("Desktop hotkeys disabled by config.");
            return Array.Empty<string>();
        }

        var errors = new List<string>();
        foreach (var pair in config.DesktopHotkeys.OrderBy(p => ParseDesktopNumberForOrdering(p.Key)))
        {
            if (!int.TryParse(pair.Key, out var desktopNumber))
            {
                errors.Add($"desktopHotkeys key invalide : {pair.Key}");
                continue;
            }

            HotkeyBinding binding;
            try
            {
                binding = HotkeyParser.Parse(desktopNumber, pair.Value);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                continue;
            }

            var id = _nextId++;
            if (!RegisterHotKey(_window.Handle, id, binding.Modifiers, binding.VirtualKey))
            {
                var error = Marshal.GetLastWin32Error();
                errors.Add($"Hotkey refusé par Windows : {binding.Text} -> bureau #{desktopNumber}. Win32Error={error}.");
                continue;
            }

            _registered[id] = binding;
            _logger.Info($"Hotkey registered: {binding.Text} -> desktop #{desktopNumber}");
        }

        if (_registered.Count == 0 && config.DesktopHotkeysEnabled)
        {
            errors.Add("Aucun hotkey DeskRealm n'a été enregistré.");
        }

        foreach (var error in errors)
        {
            _logger.Warn("Hotkey registration issue: " + error);
        }

        return errors;
    }

    public void Stop()
    {
        foreach (var id in _registered.Keys.ToList())
        {
            if (!UnregisterHotKey(_window.Handle, id))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.Warn($"Hotkey unregister failed: id={id}, Win32Error={error}");
            }
        }

        if (_registered.Count > 0)
        {
            _logger.Info($"Hotkeys unregistered: {_registered.Count}");
        }

        _registered.Clear();
    }

    private void OnHotkeyMessage(int id)
    {
        if (!_registered.TryGetValue(id, out var binding))
        {
            _logger.Warn($"WM_HOTKEY reçu pour un id inconnu : {id}");
            return;
        }

        _logger.Info($"Hotkey pressed: {binding.Text} -> desktop #{binding.DesktopNumber}");
        DesktopHotkeyPressed?.Invoke(binding.DesktopNumber, binding.Text);
    }

    private static int ParseDesktopNumberForOrdering(string key)
    {
        return int.TryParse(key, out var number) ? number : int.MaxValue;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _window.DestroyHandle();
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private sealed class HotkeyMessageWindow : NativeWindow
    {
        private readonly GlobalHotkeyService _owner;

        public HotkeyMessageWindow(GlobalHotkeyService owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams { Caption = "DeskRealmHotkeys" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                _owner.OnHotkeyMessage(m.WParam.ToInt32());
                return;
            }

            base.WndProc(ref m);
        }
    }
}
