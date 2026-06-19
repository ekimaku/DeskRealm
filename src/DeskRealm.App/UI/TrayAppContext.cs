using DeskRealm.App.Services;
using Microsoft.Win32;
using System.Diagnostics;

namespace DeskRealm.App.UI;

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly DesktopSwitchService _switchService;
    private readonly RealmConfigService _configService;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly StartupService _startupService;
    private readonly VirtualDesktopChangeMonitor _desktopChanges;
    private readonly IconLayoutWorkerClientService _iconWorker;
    private readonly FileLogger _logger;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _appIcon;
    private readonly Control _uiDispatcher = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private bool _monitoringStarted;
    private int _reconcileRequested;
    private int _reconcileRunnerActive;
    private int _registryNotificationsPending;
    private string _latestRegistryNotificationSource = "unknown";
    private DeskRealmMainForm? _mainForm;

    public TrayAppContext(
        DesktopSwitchService switchService,
        RealmConfigService configService,
        GlobalHotkeyService hotkeys,
        StartupService startupService,
        VirtualDesktopChangeMonitor desktopChanges,
        IconLayoutWorkerClientService iconWorker,
        FileLogger logger)
    {
        _switchService = switchService;
        _configService = configService;
        _hotkeys = hotkeys;
        _startupService = startupService;
        _desktopChanges = desktopChanges;
        _iconWorker = iconWorker;
        _logger = logger;

        _uiDispatcher.CreateControl();
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
        _desktopChanges.Changed += OnDesktopRegistryChanged;
        _desktopChanges.Faulted += OnDesktopMonitorFaulted;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        var waitingForFirstRunDecision = _switchService.ShouldOfferInitialDesktopImport();
        if (waitingForFirstRunDecision)
        {
            ShowDeskRealmUi(firstRun: true);
        }
        else
        {
            StartMonitoringIfNeeded();
        }

        var balloonText = waitingForFirstRunDecision
            ? "First-run setup must be completed before the first automatic switch."
            : hotkeyErrors.Count == 0
                ? "Adaptive switching is active. Desktop hotkeys are active."
                : "Adaptive switching is active. Some hotkeys were rejected; check logs.";
        _notifyIcon.ShowBalloonTip(3500, "DeskRealm", balloonText, hotkeyErrors.Count == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Open DeskRealm", null, (_, _) => ShowDeskRealmUi(firstRun: false));
        menu.Items.Add("Refresh now", null, (_, _) => QueueAction("Refresh now", () => _switchService.SwitchNow()));
        menu.Items.Add("Sync names now", null, (_, _) => QueueAction("Sync names now", () => _switchService.SyncRealmNamesNow()));
        menu.Items.Add("Save icon layout now", null, (_, _) => ConfirmAndSaveIconLayoutNow());
        menu.Items.Add("Restore icon layout now", null, (_, _) => QueueAction("Restore icon layout now", () => _switchService.RestoreIconLayoutNow()));
        menu.Items.Add("Reload hotkeys from config", null, (_, _) => SafeUiAction("Reload hotkeys", ReloadHotkeys));
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
        menu.Items.Add("Restore original Desktop", null, (_, _) => QueueAction("Restore", () => _switchService.RestoreOriginalDesktop()));
        menu.Items.Add("Quit", null, (_, _) => ExitThread());

        return menu;
    }

    private void OnDesktopHotkeyPressed(int desktopNumber, string hotkey)
    {
        QueueAction($"Hotkey {hotkey}", () => _switchService.SwitchToDesktopNumber(desktopNumber));
    }

    private void OnDesktopRegistryChanged(string source)
    {
        _latestRegistryNotificationSource = source;
        Interlocked.Increment(ref _registryNotificationsPending);
        QueueReconcile("registry-notification");
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _logger.Info("Display settings change notification received.");
        QueueReconcile("display-settings-change");
    }

    private void OnDesktopMonitorFaulted(Exception error)
    {
        _logger.Error("Virtual desktop change monitor faulted", error);
        PostToUi(() => _notifyIcon.ShowBalloonTip(
            7000,
            "DeskRealm — monitor stopped",
            error.Message,
            ToolTipIcon.Error));
    }

    private void QueueReconcile(string reason)
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        Interlocked.Exchange(ref _reconcileRequested, 1);
        if (Interlocked.CompareExchange(ref _reconcileRunnerActive, 1, 0) == 0)
        {
            _ = ReconcileLoopAsync(reason);
        }
    }

    private async Task ReconcileLoopAsync(string initialReason)
    {
        var reason = initialReason;
        try
        {
            while (!_shutdown.IsCancellationRequested && Interlocked.Exchange(ref _reconcileRequested, 0) == 1)
            {
                var notificationCount = Interlocked.Exchange(ref _registryNotificationsPending, 0);
                if (notificationCount > 0)
                {
                    _logger.Info(
                        $"Virtual desktop registry notifications coalesced: count={notificationCount}, latest={_latestRegistryNotificationSource}.");
                }

                await RunSerializedAsync($"Reconcile ({reason})", _switchService.Tick, showDialogOnError: false);
                reason = "coalesced-notification";
            }
        }
        finally
        {
            Interlocked.Exchange(ref _reconcileRunnerActive, 0);
            if (Volatile.Read(ref _reconcileRequested) == 1 && !_shutdown.IsCancellationRequested)
            {
                QueueReconcile("late-notification");
            }
        }
    }

    private void ConfirmAndSaveIconLayoutNow()
    {
        var locked = _switchService.IsCurrentLayoutOrRealmLocked();
        if (locked)
        {
            var result = MessageBox.Show(
                "This layout or its realm is locked. A manual save will replace only the active display-topology variant with the current Desktop positions. Other saved variants will remain unchanged.\n\nOverwrite the active locked variant?",
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

        QueueAction("Save icon layout now", () => _switchService.SaveIconLayoutNow(overwriteLockedLayout: locked));
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

    private void StartMonitoringIfNeeded()
    {
        if (_monitoringStarted)
        {
            return;
        }

        _desktopChanges.Start();
        _monitoringStarted = true;
        QueueReconcile("monitor-start");
    }

    private void QueueAction(string name, Action action)
    {
        _ = RunSerializedAsync(name, action, showDialogOnError: true);
    }

    private void QueueActionWithCompletion(string name, Action action, Action<Exception?> completed)
    {
        _ = RunSerializedAsync(name, action, showDialogOnError: true, completed: completed);
    }

    private async Task RunSerializedAsync(
        string name,
        Action action,
        bool showDialogOnError,
        Action<Exception?>? completed = null)
    {
        try
        {
            await _operationGate.WaitAsync(_shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            await Task.Run(action, _shutdown.Token);
            stopwatch.Stop();
            _logger.Info($"[PERF] serialized operation complete: name={name}, elapsed={stopwatch.Elapsed.TotalMilliseconds:0.0} ms.");
            PostToUi(() =>
            {
                if (_mainForm is { IsDisposed: false, Visible: true })
                {
                    _mainForm.RefreshAll();
                }

                completed?.Invoke(null);
            });
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Application shutdown.
        }
        catch (Exception ex)
        {
            _logger.Error($"Action failed: {name}", ex);
            PostToUi(() =>
            {
                _notifyIcon.ShowBalloonTip(5000, "DeskRealm — strict error", ex.Message, ToolTipIcon.Error);
                if (showDialogOnError)
                {
                    MessageBox.Show(ex.Message, "DeskRealm — error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                completed?.Invoke(ex);
            });
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void TogglePause()
    {
        QueueAction("Toggle pause", () =>
        {
            var next = !_switchService.Config.Enabled;
            _switchService.SetEnabled(next);
        });
    }

    private void ToggleStartup(ToolStripMenuItem item)
    {
        SafeUiAction("Toggle startup", () =>
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
            StartMonitoringIfNeeded,
            QueueActionWithCompletion,
            _logger);

        _mainForm.ShowFirstRunPanel(firstRun && _switchService.ShouldOfferInitialDesktopImport());
        _mainForm.RefreshAll();
        _mainForm.Show();
        _mainForm.Activate();
    }

    private void SafeUiAction(string name, Action action)
    {
        try
        {
            action();
            _mainForm?.RefreshAll();
        }
        catch (Exception ex)
        {
            _logger.Error($"UI action failed: {name}", ex);
            MessageBox.Show(ex.Message, "DeskRealm — error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PostToUi(Action action)
    {
        if (_uiDispatcher.IsDisposed || !_uiDispatcher.IsHandleCreated)
        {
            return;
        }

        try
        {
            _uiDispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // UI teardown already started.
        }
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
        _shutdown.Cancel();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _desktopChanges.Changed -= OnDesktopRegistryChanged;
        _desktopChanges.Faulted -= OnDesktopMonitorFaulted;
        _desktopChanges.Dispose();
        _hotkeys.DesktopHotkeyPressed -= OnDesktopHotkeyPressed;
        _hotkeys.Dispose();

        var operationGateAcquired = false;
        try
        {
            var shutdownGuardrailMs = Math.Max(3000, _switchService.Config.IconLayoutWorkerTimeoutMs + 1500);
            if (_operationGate.Wait(shutdownGuardrailMs))
            {
                operationGateAcquired = true;
                try
                {
                    if (_switchService.Config.RestoreDesktopOnExit)
                    {
                        _switchService.RestoreOriginalDesktop();
                    }
                }
                finally
                {
                    _operationGate.Release();
                    operationGateAcquired = false;
                }
            }
            else
            {
                throw new TimeoutException("A DeskRealm operation was still active during the strict shutdown guardrail.");
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

        var operationIdle = _operationGate.CurrentCount == 1;
        if (operationIdle)
        {
            _iconWorker.Dispose();
        }
        else
        {
            _logger.Warn("DeskRealm is exiting while a serialized operation is still active; synchronization objects are intentionally left undisposed until process termination.");
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _appIcon.Dispose();
        _mainForm?.PrepareForApplicationExit();
        _mainForm?.Dispose();
        _uiDispatcher.Dispose();
        if (!operationGateAcquired && operationIdle)
        {
            _operationGate.Dispose();
            _shutdown.Dispose();
        }
        base.ExitThreadCore();
    }
}
