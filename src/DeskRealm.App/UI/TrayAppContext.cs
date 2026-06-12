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
    private StatusForm? _statusForm;

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
        OfferInitialDesktopImportIfNeeded();
        SynchronizeStartupFromConfig(showErrors: false);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "DeskRealm",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _hotkeys.DesktopHotkeyPressed += OnDesktopHotkeyPressed;
        var hotkeyErrors = _hotkeys.Start(_switchService.Config);

        _timer = new System.Windows.Forms.Timer
        {
            Interval = _switchService.Config.PollIntervalMs
        };
        _timer.Tick += (_, _) => SafeTick();
        _timer.Start();

        SafeTick();

        var balloonText = hotkeyErrors.Count == 0
            ? "Native switch actif. Hotkeys bureaux actifs."
            : "Native switch actif. Certains hotkeys ont été refusés, voir logs.";
        _notifyIcon.ShowBalloonTip(3500, "DeskRealm", balloonText, hotkeyErrors.Count == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }



    private void OfferInitialDesktopImportIfNeeded()
    {
        if (!_switchService.ShouldOfferInitialDesktopImport())
        {
            return;
        }

        try
        {
            using var form = new InitialDesktopImportForm(
                _switchService.GetVirtualDesktopsSnapshot(),
                _switchService.GetCurrentVirtualDesktopId());

            var result = form.ShowDialog();
            if (result == DialogResult.OK)
            {
                _switchService.ImportOriginalDesktopToVirtualDesktop(
                    form.SelectedDesktopId,
                    form.LinkOriginalDesktop,
                    form.SaveLayout);

                MessageBox.Show(
                    "Desktop initial associé sans déplacer les fichiers. DeskRealm va maintenant activer le realm correspondant au bureau virtuel courant.",
                    "DeskRealm — import terminé",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _switchService.MarkInitialDesktopImportSkipped();
        }
        catch (Exception ex)
        {
            _logger.Error("Initial Desktop import wizard failed", ex);
            MessageBox.Show(
                "L'import du Desktop initial a échoué. DeskRealm ne continue pas silencieusement.\n\n" + ex.Message,
                "DeskRealm — import Desktop initial",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            throw;
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Statut", null, (_, _) => ShowStatus());
        menu.Items.Add("Refresh now", null, (_, _) => SafeAction("Refresh now", () => _switchService.SwitchNow()));
        menu.Items.Add("Sync names now", null, (_, _) => SafeAction("Sync names now", () => _switchService.SyncRealmNamesNow()));
        menu.Items.Add("Save icon layout now", null, (_, _) => SafeAction("Save icon layout now", () => _switchService.SaveIconLayoutNow()));
        menu.Items.Add("Restore icon layout now", null, (_, _) => SafeAction("Restore icon layout now", () => _switchService.RestoreIconLayoutNow()));
        menu.Items.Add("Reload hotkeys from config", null, (_, _) => SafeAction("Reload hotkeys", ReloadHotkeys));
        menu.Items.Add("Pause / Resume", null, (_, _) => TogglePause());

        var startupItem = new ToolStripMenuItem("Démarrer avec Windows")
        {
            Checked = _startupService.IsEnabledForCurrentExecutable(),
            CheckOnClick = false
        };
        startupItem.Click += (_, _) => ToggleStartup(startupItem);
        menu.Items.Add(startupItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Ouvrir realms", null, (_, _) => OpenPath(_switchService.Config.RealmsRoot!));
        menu.Items.Add("Ouvrir config", null, (_, _) => OpenPath(AppPaths.ConfigPath));
        menu.Items.Add("Ouvrir logs", null, (_, _) => OpenPath(AppPaths.LogFilePath));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Restaurer Desktop original", null, (_, _) => SafeAction("Restore", () => _switchService.RestoreOriginalDesktop()));
        menu.Items.Add("Quitter", null, (_, _) => ExitThread());

        return menu;
    }

    private void OnDesktopHotkeyPressed(int desktopNumber, string hotkey)
    {
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

    private void ReloadHotkeys()
    {
        var errors = _hotkeys.Start(_switchService.Config);
        _statusForm?.RefreshStatus();
        if (errors.Count > 0)
        {
            MessageBox.Show(
                "Certains hotkeys ont été refusés ou sont invalides :" + Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, errors),
                "DeskRealm — hotkeys",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _notifyIcon.ShowBalloonTip(2000, "DeskRealm", "Hotkeys rechargés depuis la config.", ToolTipIcon.Info);
    }

    private void SafeTick()
    {
        try
        {
            _switchService.Tick();
            _statusForm?.RefreshStatus();
        }
        catch (Exception ex)
        {
            _logger.Error("Tick failed", ex);
            _timer.Stop();
            _notifyIcon.ShowBalloonTip(
                5000,
                "DeskRealm — erreur stricte",
                ex.Message,
                ToolTipIcon.Error);
        }
    }

    private void SafeAction(string name, Action action)
    {
        try
        {
            action();
            _statusForm?.RefreshStatus();
        }
        catch (Exception ex)
        {
            _logger.Error($"Action failed: {name}", ex);
            MessageBox.Show(ex.Message, "DeskRealm — erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            _notifyIcon.ShowBalloonTip(2000, "DeskRealm", next ? "Démarrage Windows activé." : "Démarrage Windows désactivé.", ToolTipIcon.Info);
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
                MessageBox.Show(ex.Message, "DeskRealm — démarrage Windows", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void ShowStatus()
    {
        _statusForm ??= new StatusForm(_switchService);
        _statusForm.RefreshStatus();
        _statusForm.Show();
        _statusForm.Activate();
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

        MessageBox.Show($"Chemin introuvable : {path}", "DeskRealm", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                "DeskRealm n'a pas pu restaurer automatiquement le Desktop original. Lance scripts\\Restore-Desktop.ps1.\n\n" + ex.Message,
                "DeskRealm — restore échoué",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _statusForm?.Dispose();
        base.ExitThreadCore();
    }
}
