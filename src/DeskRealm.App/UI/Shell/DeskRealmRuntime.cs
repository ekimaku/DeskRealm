// DeskRealm-RealmStudio-Schema: v0.7.0
using DeskRealm.App.Interop;
using DeskRealm.App.Services;
using System.Diagnostics;

namespace DeskRealm.App.Shell;

/// <summary>
/// Owns the system-level DeskRealm lifecycle. WinUI views only request operations;
/// this class keeps the former tray orchestration, lock and strict-error semantics
/// independent from the visual framework.
/// </summary>
internal sealed class DeskRealmRuntime : IDisposable
{
    private readonly DesktopSwitchService _switchService;
    private readonly RealmConfigService _configService;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly StartupService _startupService;
    private readonly VirtualDesktopChangeMonitor _desktopChanges;
    private readonly IconLayoutWorkerClientService _iconWorker;
    private readonly ExplorerRestartService _explorerRestart;
    private readonly FileLogger _logger;
    private readonly Action<Action> _postToUi;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private NativeTrayIconService? _tray;
    private bool _monitoringStarted;
    private bool _started;
    private bool _hotkeysSuspendedForCapture;
    private int _reconcileRequested;
    private int _reconcileRunnerActive;
    private int _registryNotificationsPending;
    private string _latestRegistryNotificationSource = "unknown";
    private bool _disposed;

    public event Action? StateChanged;
    public event Action<string>? StrictOperationFailed;

    private DeskRealmRuntime(
        DesktopSwitchService switchService,
        RealmConfigService configService,
        GlobalHotkeyService hotkeys,
        StartupService startupService,
        VirtualDesktopChangeMonitor desktopChanges,
        IconLayoutWorkerClientService iconWorker,
        ExplorerRestartService explorerRestart,
        FileLogger logger,
        Action<Action> postToUi)
    {
        _switchService = switchService;
        _configService = configService;
        _hotkeys = hotkeys;
        _startupService = startupService;
        _desktopChanges = desktopChanges;
        _iconWorker = iconWorker;
        _explorerRestart = explorerRestart;
        _logger = logger;
        _postToUi = postToUi;
        _switchService.IconLayoutRecoveryScheduled += OnIconLayoutRecoveryScheduled;
    }

    public static DeskRealmRuntime Create(FileLogger logger, Action<Action> postToUi)
    {
        var configService = new RealmConfigService(logger);
        var knownFolder = new KnownFolderService(logger);
        var virtualDesktop = new VirtualDesktopRegistryService(logger);
        var shellRefresh = new ShellRefreshService(logger);
        var iconLayouts = new IconLayoutWorkerClientService(logger);
        var keyboard = new KeyboardInputService(logger);
        var navigator = new VirtualDesktopNavigatorService(keyboard, virtualDesktop, logger);
        var wallpapers = new WallpaperService(logger);
        var switchService = new DesktopSwitchService(configService, knownFolder, virtualDesktop, shellRefresh, iconLayouts, navigator, keyboard, wallpapers, logger);
        return new DeskRealmRuntime(
            switchService,
            configService,
            new GlobalHotkeyService(logger),
            new StartupService(logger),
            new VirtualDesktopChangeMonitor(logger),
            iconLayouts,
            new ExplorerRestartService(logger),
            logger,
            postToUi);
    }

    public void BindWindow(nint windowHandle, Action showWindow, Action exitApplication)
    {
        if (_tray is not null) throw new InvalidOperationException("DeskRealm runtime window binding was requested twice.");
        _tray = new NativeTrayIconService(
            _logger,
            new NativeTrayMenuActions(
                showWindow,
                () => _ = RunSerializedAsync("Refresh realm from tray", _switchService.SwitchNow),
                () => _ = SyncRealmNamesAsync(),
                () => _ = SaveIconLayoutAsync(),
                () => _ = RestoreIconLayoutAsync(),
                () => _ = ReloadHotkeysAsync(),
                () => _ = ToggleAutomationAsync(),
                () => _startupService.IsEnabledForCurrentExecutable(),
                () => _ = ToggleStartWithWindowsAsync(),
                () => OpenPath(_switchService.Config.RealmsRoot ?? throw new InvalidOperationException("Realms root is unavailable.")),
                () => OpenPath(AppPaths.ConfigPath),
                () => OpenPath(AppPaths.LogFilePath),
                () => _ = RunSerializedAsync("Restore original Desktop from tray", _switchService.RestoreOriginalDesktop),
                exitApplication));
        _tray.DisplayTopologyChanged += OnNativeDisplayTopologyChanged;
        _tray.Initialize(windowHandle);
    }

    public DeskRealmLaunchState Start()
    {
        if (_started) throw new InvalidOperationException("DeskRealm runtime was started twice.");
        _switchService.Initialize();
        SynchronizeStartupFromConfig(showErrors: false);

        _hotkeys.RealmHotkeyPressed += OnRealmHotkeyPressed;
        var hotkeyErrors = _hotkeys.Start(_switchService.Config);
        _desktopChanges.Changed += OnDesktopRegistryChanged;
        _desktopChanges.Faulted += OnDesktopMonitorFaulted;
        _started = true;

        var needsImport = _switchService.ShouldOfferInitialDesktopImport();
        if (!needsImport)
        {
            StartMonitoringIfNeeded();
            _ = RunSerializedAsync("Switch to configured default realm", _switchService.SwitchToDefaultRealmIfConfigured, reportFailure: false);
        }

        var balloon = needsImport
            ? "First-run setup must be completed before the first automatic switch."
            : hotkeyErrors.Count == 0
                ? "Adaptive realm switching is active. Realm hotkeys are active."
                : "Adaptive switching is active. Some realm hotkeys were rejected; check Realm Studio diagnostics.";
        _tray?.ShowInfo("DeskRealm", balloon, hotkeyErrors.Count > 0);
        return new DeskRealmLaunchState(needsImport, Config.StartMinimized, hotkeyErrors);
    }

    public IReadOnlyList<VirtualDesktopInfo> GetVirtualDesktops() => _switchService.GetVirtualDesktopsSnapshot();
    public RealmNameAvailability GetRealmNameAvailability(Guid desktopId, string displayName) => _switchService.GetRealmNameAvailability(desktopId, displayName);
    public string? GetRealmHotkey(Guid desktopId) => _switchService.GetRealmHotkey(desktopId);
    public RealmConfig Config => _switchService.Config;

    public async Task<RealmStudioSnapshot> GetRealmStudioSnapshotAsync()
    {
        try
        {
            await _operationGate.WaitAsync(_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return RealmStudioSnapshot.Empty;
        }

        try
        {
            return await Task.Run(BuildRealmStudioSnapshot, _shutdown.Token);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private RealmStudioSnapshot BuildRealmStudioSnapshot()
    {
        var desktops = _switchService.GetVirtualDesktopsSnapshot().OrderBy(desktop => desktop.Number).ToList();
        var currentId = _switchService.GetCurrentVirtualDesktopId();
        var lockRealms = _switchService.GetIconLayoutLockSnapshot();
        var status = _switchService.GetStatus();
        var realmSnapshots = new List<RealmStudioRealmSnapshot>(desktops.Count);

        foreach (var desktop in desktops)
        {
            var layoutRealm = lockRealms.FirstOrDefault(realm => realm.Layouts.Any(layout => layout.DesktopId == desktop.Id));
            var layout = layoutRealm?.Layouts.FirstOrDefault(item => item.DesktopId == desktop.Id);
            var profile = _switchService.GetRealmProfile(desktop.Id);
            var hotkey = _switchService.GetRealmHotkey(desktop.Id);
            var wallpaper = _switchService.SynchronizeRealmWallpaperFromWindows(desktop.Id);
            var variants = layout?.Variants ?? [];
            var currentVariant = variants.FirstOrDefault(variant => variant.IsCurrentTopology && variant.HasSavedLayout);
            var savedIconCount = currentVariant?.IconCount ?? 0;
            var hasCurrentLayoutSnapshot = currentVariant is not null;
            var variantCount = variants.Count(variant => variant.HasSavedLayout);

            realmSnapshots.Add(new RealmStudioRealmSnapshot(
                desktop.Id,
                desktop.Number,
                desktop.Name,
                desktop.Id == currentId,
                layoutRealm?.RealmPath ?? string.Empty,
                _switchService.IsNativeDesktopRealm(desktop.Id),
                profile.IsFavorite,
                hotkey,
                wallpaper.PreviewPath,
                wallpaper.DisplayName,
                wallpaper.Status,
                wallpaper.HasPreview,
                layout?.IsLayoutLocked ?? false,
                layoutRealm?.IsLocked ?? false,
                layout?.EffectiveLocked ?? false,
                hasCurrentLayoutSnapshot,
                variantCount,
                savedIconCount,
                variants));
        }

        return new RealmStudioSnapshot(
            realmSnapshots,
            status,
            _switchService.IconLayoutRuntimeStatus,
            _startupService.IsEnabledForCurrentExecutable(),
            _switchService.Config.Enabled,
            _switchService.Config.DesktopHotkeysEnabled,
            _switchService.Config.StartWithWindows,
            _switchService.Config.StartMinimized,
            _switchService.Config.RealmRenameApplyMode);
    }

    public async Task SwitchToRealmAsync(Guid desktopId) => await RunSerializedAsync("Switch realm", () => _switchService.SwitchToDesktop(desktopId));
    public async Task SetDefaultRealmAsync(Guid desktopId) => await RunSerializedAsync("Set default realm", () => _switchService.SetDefaultRealm(desktopId));
    public async Task ToggleRealmLockAsync(Guid desktopId) => await RunSerializedAsync("Toggle realm lock", () => _switchService.ToggleRealmLock(desktopId));
    public async Task UpdateRealmHotkeyAsync(Guid desktopId, string? hotkey) => await RunSerializedAsync("Save inline realm hotkey", () =>
    {
        _switchService.UpdateRealmHotkey(desktopId, hotkey);
        if (_hotkeysSuspendedForCapture)
        {
            _logger.Info("Inline realm hotkey saved while capture is open; registration will resume when capture closes.");
            return;
        }

        ReloadHotkeysInternal();
    });
    public async Task SetRealmWallpaperAsync(Guid desktopId, string sourcePath) => await RunSerializedAsync("Save inline realm wallpaper", () => _switchService.SetRealmWallpaper(desktopId, sourcePath));
    public async Task SaveIconLayoutAsync(bool overwriteLockedLayout = false) => await RunSerializedAsync("Save current icon layout", () => _switchService.SaveIconLayoutNow(overwriteLockedLayout));
    public async Task RestoreIconLayoutAsync() => await RunSerializedAsync("Restore current icon layout", _switchService.RestoreIconLayoutNow);
    public async Task SyncRealmNamesAsync() => await RunSerializedAsync("Sync realm names", _switchService.SyncRealmNamesNow);

    public async Task SuspendGlobalHotkeysForCaptureAsync()
    {
        await RunSerializedAsync("Suspend global hotkeys for capture", () =>
        {
            if (_hotkeysSuspendedForCapture) return;
            _hotkeys.Stop();
            _hotkeysSuspendedForCapture = true;
            _logger.Info("Global realm hotkeys suspended while the Realm Studio capture field is open.");
        });
    }

    public async Task ResumeGlobalHotkeysAfterCaptureAsync()
    {
        await RunSerializedAsync("Resume global hotkeys after capture", () =>
        {
            if (!_hotkeysSuspendedForCapture) return;
            _hotkeysSuspendedForCapture = false;
            var errors = ReloadHotkeysInternal(throwOnFailure: false);
            if (errors.Count == 0)
            {
                _logger.Info("Global realm hotkeys restored after the Realm Studio capture field closed.");
            }
            else
            {
                _logger.Warn($"Global realm hotkeys resumed after capture with {errors.Count} registration issue(s). Realm Studio status remains explicit; the editor is not crashed by a recoverable registration report.");
            }
        });
    }

    public async Task SaveRealmStudioEditAsync(Guid desktopId, RealmStudioEditRequest request)
    {
        await RunSerializedAsync("Save Realm Studio modal", () =>
        {
            var currentDesktop = _switchService.GetVirtualDesktopsSnapshot().FirstOrDefault(desktop => desktop.Id == desktopId)
                ?? throw new InvalidOperationException($"Cannot save Realm Studio edits for missing Windows virtual desktop {desktopId:B}.");
            var displayNameChanged = !string.Equals(currentDesktop.Name, request.DisplayName, StringComparison.Ordinal);

            if (displayNameChanged)
            {
                if (request.RenameApplyMode == RealmRenameApplyMode.Ask)
                {
                    throw new InvalidOperationException("Realm rename requires an explicit Windows Shell apply choice.");
                }

                _switchService.RenameRealm(desktopId, request.DisplayName, request.DuplicateResolution);
            }
            else
            {
                _logger.Info($"Realm Studio display-name save skipped: unchanged value '{request.DisplayName}' for {desktopId:B}.");
            }

            _switchService.UpdateRealmProfile(desktopId, request.IsDefaultRealm, request.IsDefaultRealm, request.Hotkey);

            if (request.ClearWallpaper)
            {
                _switchService.ClearRealmWallpaper(desktopId);
            }
            else if (!string.IsNullOrWhiteSpace(request.WallpaperSourcePath))
            {
                _switchService.SetRealmWallpaper(desktopId, request.WallpaperSourcePath);
            }

            if (request.RealmLocked) _switchService.LockRealmLayoutsForDesktop(desktopId);
            else _switchService.UnlockRealmLayoutsForDesktop(desktopId);
            if (request.LayoutLocked) _switchService.LockIconLayout(desktopId);
            else _switchService.UnlockIconLayout(desktopId);

            foreach (var variant in request.VariantLocks)
            {
                if (variant.Value) _switchService.LockIconLayoutVariant(desktopId, variant.Key);
                else _switchService.UnlockIconLayoutVariant(desktopId, variant.Key);
            }
            foreach (var topologyKey in request.DeleteVariants.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _switchService.DeleteIconLayoutVariant(desktopId, topologyKey);
            }

            if (request.RememberRenameApplyMode)
            {
                _switchService.Config.RealmRenameApplyMode = request.RenameApplyMode;
                _configService.Save(_switchService.Config);
                _logger.Info($"Realm rename apply behavior remembered: {request.RenameApplyMode}.");
            }

            if (_hotkeysSuspendedForCapture)
            {
                _logger.Info("Realm hotkey configuration saved while capture is open; registration will resume when the editor closes.");
            }
            else
            {
                ReloadHotkeysInternal();
            }

            if (!displayNameChanged) return;

            if (request.RenameApplyMode == RealmRenameApplyMode.NextReboot)
            {
                _logger.Info(
                    $"Windows virtual desktop name persisted for {desktopId:B}; Explorer restart was intentionally deferred until the next reboot/manual shell restart.");
                return;
            }

            try
            {
                _explorerRestart.RestartCurrentSessionExplorer();
                _logger.Info($"Windows virtual desktop name applied through explicit Explorer restart: {desktopId:B}.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "The Windows virtual desktop name was saved to Explorer Registry metadata, but Explorer could not be restarted. " +
                    "Task View will show the new label after the next successful Explorer start or reboot. " +
                    ex.Message,
                    ex);
            }
        });
    }

    public async Task<VirtualDesktopInfo> CreateVirtualDesktopAsync()
        => await RunSerializedAsync("Create Windows virtual desktop", _switchService.CreateVirtualDesktop);

    public async Task DeleteVirtualDesktopAsync(Guid desktopId)
        => await RunSerializedAsync("Delete Windows virtual desktop", () => _switchService.DeleteVirtualDesktop(desktopId));


    public async Task ApplyGlobalSettingsAsync(
        bool enabled,
        bool startWithWindows,
        bool startMinimized,
        bool hotkeysEnabled,
        bool rememberRealmRenameApplyMode,
        bool restartExplorerAfterRealmRename)
    {
        await RunSerializedAsync("Apply global settings", () =>
        {
            _switchService.SetEnabled(enabled);
            var startupEnabled = _startupService.IsEnabledForCurrentExecutable();
            if (startWithWindows && !startupEnabled) _startupService.Enable();
            else if (!startWithWindows && startupEnabled) _startupService.Disable();
            _switchService.Config.StartWithWindows = startWithWindows;
            _switchService.Config.StartMinimized = startMinimized;
            _switchService.Config.DesktopHotkeysEnabled = hotkeysEnabled;
            _switchService.Config.RealmRenameApplyMode = rememberRealmRenameApplyMode
                ? restartExplorerAfterRealmRename
                    ? RealmRenameApplyMode.RestartExplorer
                    : RealmRenameApplyMode.NextReboot
                : RealmRenameApplyMode.Ask;
            _configService.Save(_switchService.Config);
            _logger.Info($"Automation settings saved: startMinimized={_switchService.Config.StartMinimized}; realmRenameApplyMode={_switchService.Config.RealmRenameApplyMode}.");
            ReloadHotkeysInternal();
        });
    }

    public async Task CompleteInitialImportAsync(Guid desktopId, bool saveLayout)
    {
        await RunSerializedAsync("Associate original Windows Desktop", () => _switchService.ImportOriginalDesktopToVirtualDesktop(desktopId, saveLayout));
        StartMonitoringIfNeeded();
    }

    public async Task SkipInitialImportAsync()
    {
        await RunSerializedAsync("Skip initial Desktop import", () => _ = _switchService.SkipInitialDesktopImportAndCreateOriginalDesktopShortcuts());
        StartMonitoringIfNeeded();
    }

    public async Task ToggleAutomationAsync()
    {
        await RunSerializedAsync("Toggle realm automation", () => _switchService.SetEnabled(!_switchService.Config.Enabled));
    }

    public async Task ReloadHotkeysAsync()
    {
        // ReloadHotkeysInternal returns the registration report so the capture-resume path can
        // keep recovery non-blocking. The explicit UI command remains strict, but this Action
        // intentionally discards the successful report after ReloadHotkeysInternal has thrown
        // for any genuine registration failure.
        await RunSerializedAsync("Reload realm hotkeys", () =>
        {
            _ = ReloadHotkeysInternal();
        });
    }

    public async Task ToggleStartWithWindowsAsync()
    {
        await RunSerializedAsync("Toggle Start with Windows", () =>
        {
            var currentlyEnabled = _startupService.IsEnabledForCurrentExecutable();
            if (currentlyEnabled) _startupService.Disable();
            else _startupService.Enable();

            _switchService.Config.StartWithWindows = !currentlyEnabled;
            _configService.Save(_switchService.Config);
        });
    }

    public void OpenPath(string path)
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
        throw new FileNotFoundException($"Path not found: {path}", path);
    }

    private async Task<T> RunSerializedAsync<T>(string name, Func<T> action, bool reportFailure = true)
    {
        try
        {
            await _operationGate.WaitAsync(_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            throw new OperationCanceledException("DeskRealm is shutting down.");
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await Task.Run(action, _shutdown.Token);
            stopwatch.Stop();
            _logger.Info($"[PERF] serialized operation complete: name={name}, elapsed={stopwatch.Elapsed.TotalMilliseconds:0.0} ms.");
            PostStateChanged();
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error($"Action failed: {name}", ex);
            if (reportFailure) _postToUi(() => StrictOperationFailed?.Invoke(ex.Message));
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task RunSerializedAsync(string name, Action action, bool reportFailure = true)
    {
        try
        {
            await _operationGate.WaitAsync(_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            await Task.Run(action, _shutdown.Token);
            stopwatch.Stop();
            _logger.Info($"[PERF] serialized operation complete: name={name}, elapsed={stopwatch.Elapsed.TotalMilliseconds:0.0} ms.");
            PostStateChanged();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Explicit application shutdown.
        }
        catch (Exception ex)
        {
            _logger.Error($"Action failed: {name}", ex);
            if (reportFailure)
            {
                _postToUi(() => StrictOperationFailed?.Invoke(ex.Message));
            }
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private IReadOnlyList<string> ReloadHotkeysInternal(bool throwOnFailure = true)
    {
        if (_hotkeysSuspendedForCapture)
        {
            _logger.Info("Realm hotkey registration reload deferred because a Realm Studio capture is active.");
            return Array.Empty<string>();
        }

        var errors = _hotkeys.Start(_switchService.Config);
        if (errors.Count == 0) return errors;
        if (throwOnFailure)
        {
            throw new InvalidOperationException("Some realm hotkeys were rejected or are invalid:" + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
        return errors;
    }

    private void StartMonitoringIfNeeded()
    {
        if (_monitoringStarted) return;
        _desktopChanges.Start();
        _monitoringStarted = true;
        QueueReconcile("monitor-start");
    }

    private void OnRealmHotkeyPressed(Guid desktopId, string text)
    {
        if (_hotkeysSuspendedForCapture)
        {
            _logger.Warn($"Ignored late WM_HOTKEY '{text}' while Realm Studio owns keyboard capture.");
            return;
        }

        _ = RunSerializedAsync($"Realm hotkey {text}", () => _switchService.SwitchToDesktop(desktopId));
    }

    private void OnDesktopRegistryChanged(string source)
    {
        _latestRegistryNotificationSource = source;
        Interlocked.Increment(ref _registryNotificationsPending);
        QueueReconcile("registry:" + source);
    }

    private void OnDesktopMonitorFaulted(Exception exception)
    {
        _logger.Error("Virtual desktop monitor fault", exception);
        _postToUi(() => StrictOperationFailed?.Invoke("Windows virtual-desktop monitor failed: " + exception.Message));
    }

    private void OnNativeDisplayTopologyChanged()
    {
        QueueReconcile("wm-displaychange");
    }

    private void OnIconLayoutRecoveryScheduled(TimeSpan delay)
    {
        _ = QueueShellReadinessRecoveryAsync(delay);
    }

    private async Task QueueShellReadinessRecoveryAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _shutdown.Token);
            QueueReconcile("shell-readiness-retry");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Explicit shutdown: no delayed reconciliation may survive it.
        }
    }

    private void QueueReconcile(string reason)
    {
        if (_shutdown.IsCancellationRequested) return;
        Interlocked.Exchange(ref _reconcileRequested, 1);
        if (Interlocked.CompareExchange(ref _reconcileRunnerActive, 1, 0) != 0) return;
        _ = RunReconcileLoopAsync(reason);
    }

    private async Task RunReconcileLoopAsync(string reason)
    {
        try
        {
            while (Interlocked.Exchange(ref _reconcileRequested, 0) == 1 && !_shutdown.IsCancellationRequested)
            {
                var notificationCount = Interlocked.Exchange(ref _registryNotificationsPending, 0);
                var source = _latestRegistryNotificationSource;
                var tickReason = notificationCount > 0 ? $"{reason}; registryNotifications={notificationCount}; latest={source}" : reason;
                try
                {
                    await RunSerializedAsync("Reconcile " + tickReason, _switchService.Tick, reportFailure: false);
                }
                catch (Exception ex)
                {
                    _logger.Error("Reconciliation failed", ex);
                    _postToUi(() => StrictOperationFailed?.Invoke(ex.Message));
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _reconcileRunnerActive, 0);
            if (Volatile.Read(ref _reconcileRequested) == 1 && !_shutdown.IsCancellationRequested) QueueReconcile("late-notification");
        }
    }

    private void SynchronizeStartupFromConfig(bool showErrors)
    {
        try
        {
            var enabled = _startupService.IsEnabledForCurrentExecutable();
            if (_switchService.Config.StartWithWindows && !enabled) _startupService.Enable();
            else if (!_switchService.Config.StartWithWindows && enabled) _startupService.Disable();
        }
        catch (Exception ex)
        {
            _logger.Error("Startup sync failed", ex);
            if (showErrors) _postToUi(() => StrictOperationFailed?.Invoke(ex.Message));
        }
    }

    private void PostStateChanged() => _postToUi(() => StateChanged?.Invoke());

    public void Dispose()
    {
        if (_disposed) return;
        _shutdown.Cancel();
        _switchService.IconLayoutRecoveryScheduled -= OnIconLayoutRecoveryScheduled;
        _desktopChanges.Changed -= OnDesktopRegistryChanged;
        _desktopChanges.Faulted -= OnDesktopMonitorFaulted;
        _desktopChanges.Dispose();
        _hotkeys.RealmHotkeyPressed -= OnRealmHotkeyPressed;
        _hotkeys.Dispose();

        var acquired = false;
        try
        {
            // A XAML/Window construction failure can call App.ExitDeskRealm() after
            // DeskRealmRuntime.Create(), but before Start() initializes configuration.
            // No realm switch has been started in that state, so restore is both unsafe
            // and meaningless. Dispose the owned services without reading Config.
            if (_started)
            {
                var config = _switchService.Config;
                var guardrailMs = Math.Max(3000, config.IconLayoutWorkerTimeoutMs + 1500);
                if (!_operationGate.Wait(guardrailMs)) throw new TimeoutException("A DeskRealm operation was still active during the strict shutdown guardrail.");
                acquired = true;
                if (config.RestoreDesktopOnExit) _switchService.RestoreOriginalDesktop();
            }
            else
            {
                _logger.Info("DeskRealm runtime stopped before Start completed; Desktop restore was intentionally skipped because configuration was not guaranteed to be initialized.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Restore on exit failed", ex);
            NativeMessageBox.Show("DeskRealm could not automatically restore the original Desktop. Run scripts\\Restore-Desktop.ps1.\n\n" + ex.Message, "DeskRealm — restore failed", NativeMessageBox.Icon.Error);
        }
        finally
        {
            if (acquired) _operationGate.Release();
            if (_tray is not null)
            {
                _tray.DisplayTopologyChanged -= OnNativeDisplayTopologyChanged;
                _tray.Dispose();
            }
            _iconWorker.Dispose();
            _shutdown.Dispose();
            _operationGate.Dispose();
            _disposed = true;
        }
    }
}

internal sealed record DeskRealmLaunchState(
    bool RequiresInitialDesktopImport,
    bool StartMinimized,
    IReadOnlyList<string> HotkeyRegistrationErrors);


internal sealed record RealmStudioSnapshot(
    IReadOnlyList<RealmStudioRealmSnapshot> Realms,
    DesktopSwitchStatus Status,
    string IconLayoutRuntimeStatus,
    bool WindowsStartupRegistered,
    bool AutomationEnabled,
    bool HotkeysEnabled,
    bool StartWithWindows,
    bool StartMinimized,
    RealmRenameApplyMode RealmRenameApplyPolicy)
{
    public static RealmStudioSnapshot Empty { get; } = new([], new DesktopSwitchStatus("Unavailable", string.Empty, string.Empty, string.Empty, DateTimeOffset.MinValue, "DeskRealm is shutting down.", string.Empty), "Unavailable", false, false, false, false, true, RealmRenameApplyMode.Ask);
}

internal sealed record RealmStudioRealmSnapshot(
    Guid DesktopId,
    int DesktopNumber,
    string DesktopName,
    bool IsCurrent,
    string RealmPath,
    bool IsNativeDesktopRealm,
    bool IsFavorite,
    string? Hotkey,
    string WallpaperPath,
    string WallpaperFileName,
    string WallpaperStatus,
    bool HasWallpaperPreview,
    bool IsLayoutLocked,
    bool IsRealmLocked,
    bool EffectiveLocked,
    bool HasSavedLayout,
    int VariantCount,
    int SavedIconCount,
    IReadOnlyList<IconLayoutVariantSnapshot> Variants);
