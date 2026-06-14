using DeskRealm.App.Services;
using System.Diagnostics;

namespace DeskRealm.App.UI;

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly DesktopSwitchService _switchService;
    private readonly RealmConfigService _configService;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly StartupService _startupService;
    private readonly FileLogger _logger;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _appIcon;
    private bool _pollingStarted;
    private DeskRealmMainForm? _mainForm;

    public TrayAppContext(
        DesktopSwitchService switchService,
        RealmConfigService configService,
        GlobalHotkeyService hotkeys,
        StartupService startupService,
        FileLogger logger)
    {
        _switchService = switchService;
        _configService = configService;
        _hotkeys = hotkeys;
        _startupService = startupService;
        _logger = logger;

        _switchService.Initialize();
        SynchronizeStartupFromConfig(showErrors: false);

        _appIcon = DeskRealmIcon.Load(_logger);
        _notifyIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "DeskRealm",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowDeskRealmUi(firstRun: false);

        _hotkeys.DesktopHotkeyPressed += OnDesktopHotkeyPressed;
        var hotkeyErrors = _hotkeys.Start(_switchService.Config);

        _timer = new System.Windows.Forms.Timer
        {
            Interval = _switchService.Config.PollIntervalMs
        };
        _timer.Tick += (_, _) => SafeTick();

        var waitingForFirstRunDecision = _switchService.ShouldOfferInitialDesktopImport();
        if (waitingForFirstRunDecision)
        {
            ShowDeskRealmUi(firstRun: true);
        }
        else
        {
            StartPollingIfNeeded();
        }

        var balloonText = waitingForFirstRunDecision
            ? "First-run setup must be completed before the first automatic switch."
            : hotkeyErrors.Count == 0
                ? "Native switching is active. Desktop hotkeys are active."
                : "Native switching is active. Some hotkeys were rejected; check logs.";
        _notifyIcon.ShowBalloonTip(3500, "DeskRealm", balloonText, hotkeyErrors.Count == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Open DeskRealm", null, (_, _) => ShowDeskRealmUi(firstRun: false));
        menu.Items.Add("Refresh now", null, (_, _) => SafeAction("Refresh now", () => _switchService.SwitchNow()));
        menu.Items.Add("Sync names now", null, (_, _) => SafeAction("Sync names now", () => _switchService.SyncRealmNamesNow()));
        menu.Items.Add("Save icon layout now", null, (_, _) => SafeAction("Save icon layout now", ConfirmAndSaveIconLayoutNow));
        menu.Items.Add("Restore icon layout now", null, (_, _) => SafeAction("Restore icon layout now", () => _switchService.RestoreIconLayoutNow()));
        menu.Items.Add("Reload hotkeys from config", null, (_, _) => SafeAction("Reload hotkeys", ReloadHotkeys));
        menu.Items.Add("Pause / Resume", null, (_, _) => TogglePause());

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = _startupService.IsEnabledForCurrentExecutable(),
            CheckOnClick = false
        };
        startupItem.Click += (_, _) => ToggleStartup(startupItem);
        menu.Items.Add(startupItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open realms", null, (_, _) => OpenPath(_switchService.Config.RealmsRoot!));
        menu.Items.Add("Open config", null, (_, _) => OpenPath(AppPaths.ConfigPath));
        menu.Items.Add("Open logs", null, (_, _) => OpenPath(AppPaths.LogFilePath));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Restore original Desktop", null, (_, _) => SafeAction("Restore", () => _switchService.RestoreOriginalDesktop()));
        menu.Items.Add("Quit", null, (_, _) => ExitThread());

        return menu;
    }

    private void OnDesktopHotkeyPressed(int desktopNumber, string hotkey)
    {
        if (!_switchService.Config.Enabled)
        {
            _logger.Info($"Hotkey ignored while DeskRealm is disabled: {hotkey} -> desktop #{desktopNumber}");
            _notifyIcon.ShowBalloonTip(2000, "DeskRealm", "DeskRealm is paused. Enable realm switching automation to use desktop hotkeys.", ToolTipIcon.Info);
            _mainForm?.RefreshAll();
            return;
        }

        SafeAction($"Hotkey {hotkey}", () =>
        {
            // WM_HOTKEY is raised while the triggering keys may still be physically down.
            // A tiny delay avoids turning the synthetic Win+Ctrl+Arrow into Win+Ctrl+Shift+Arrow
            // when the configured hotkey itself uses Shift.
            if (_switchService.Config.HotkeyInitialDelayMs > 0)
            {
                Thread.Sleep(_switchService.Config.HotkeyInitialDelayMs);
            }

            _switchService.SwitchToDesktopNumber(desktopNumber);
        });
    }

    private void ConfirmAndSaveIconLayoutNow()
    {
        var locked = _switchService.IsCurrentLayoutOrRealmLocked();
        if (locked)
        {
            var result = MessageBox.Show(
                "This layout or its realm is locked. A manual save will replace the protected positions with the current Desktop state.\n\nOverwrite the locked layout?",
                "DeskRealm — locked layout",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (result != DialogResult.Yes)
            {
                _logger.Info("Locked icon layout manual overwrite cancelled from tray.");
                return;
            }
        }

        _switchService.SaveIconLayoutNow(overwriteLockedLayout: locked);
    }

    private void ReloadHotkeys()
    {
        var errors = _hotkeys.Start(_switchService.Config);
        _mainForm?.RefreshAll();
        if (errors.Count > 0)
        {
            MessageBox.Show(
                "Some hotkeys were rejected or are invalid:" + Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, errors),
                "DeskRealm — hotkeys",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _notifyIcon.ShowBalloonTip(2000, "DeskRealm", "Hotkeys reloaded from config.", ToolTipIcon.Info);
    }

    private void StartPollingIfNeeded()
    {
        if (_pollingStarted)
        {
            return;
        }

        _pollingStarted = true;
        _timer.Start();
        SafeTick();
    }

    private void SafeTick()
    {
        try
        {
            _switchService.Tick();
            _mainForm?.RefreshStatus();
        }
        catch (Exception ex)
        {
            _logger.Error("Tick failed", ex);
            _timer.Stop();
            _notifyIcon.ShowBalloonTip(
                5000,
                "DeskRealm — strict error",
                ex.Message,
                ToolTipIcon.Error);
        }
    }

    private void SafeAction(string name, Action action)
    {
        try
        {
            action();
            _mainForm?.RefreshAll();
        }
        catch (Exception ex)
        {
            _logger.Error($"Action failed: {name}", ex);
            MessageBox.Show(ex.Message, "DeskRealm — error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void TogglePause()
    {
        SafeAction("Toggle pause", () =>
        {
            var next = !_switchService.Config.Enabled;
            _switchService.SetEnabled(next);
            if (next && !_timer.Enabled)
            {
                _timer.Start();
            }
        });
    }

    private void ToggleStartup(ToolStripMenuItem item)
    {
        SafeAction("Toggle startup", () =>
        {
            var next = !_startupService.IsEnabledForCurrentExecutable();
            if (next)
            {
                _startupService.Enable();
            }
            else
            {
                _startupService.Disable();
            }

            _switchService.Config.StartWithWindows = next;
            _configService.Save(_switchService.Config);
            item.Checked = next;
            _notifyIcon.ShowBalloonTip(2000, "DeskRealm", next ? "Windows startup enabled." : "Windows startup disabled.", ToolTipIcon.Info);
        });
    }

    private void SynchronizeStartupFromConfig(bool showErrors)
    {
        try
        {
            var enabled = _startupService.IsEnabledForCurrentExecutable();
            if (_switchService.Config.StartWithWindows && !enabled)
            {
                _startupService.Enable();
            }
            else if (!_switchService.Config.StartWithWindows && enabled)
            {
                _startupService.Disable();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Startup sync failed", ex);
            if (showErrors)
            {
                MessageBox.Show(ex.Message, "DeskRealm — Windows startup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void ShowDeskRealmUi(bool firstRun)
    {
        _mainForm ??= new DeskRealmMainForm(
            _switchService,
            _configService,
            _startupService,
            ReloadHotkeys,
            ExitThread,
            StartPollingIfNeeded,
            _logger);

        _mainForm.ShowFirstRunPanel(firstRun && _switchService.ShouldOfferInitialDesktopImport());
        _mainForm.Show();
        _mainForm.Activate();
    }

    private static void OpenPath(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return;
        }

        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }

        MessageBox.Show($"Path not found: {path}", "DeskRealm", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _hotkeys.DesktopHotkeyPressed -= OnDesktopHotkeyPressed;
        _hotkeys.Dispose();

        try
        {
            if (_switchService.Config.RestoreDesktopOnExit)
            {
                _switchService.RestoreOriginalDesktop();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Restore on exit failed", ex);
            MessageBox.Show(
                "DeskRealm could not automatically restore the original Desktop. Run scripts\\Restore-Desktop.ps1.\n\n" + ex.Message,
                "DeskRealm — restore failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _appIcon.Dispose();
        _mainForm?.PrepareForApplicationExit();
        _mainForm?.Dispose();
        base.ExitThreadCore();
    }
}
