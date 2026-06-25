using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DeskRealm.App.Services;

internal sealed class DesktopSwitchService
{
    private readonly RealmConfigService _configService;
    private readonly KnownFolderService _knownFolder;
    private readonly VirtualDesktopRegistryService _virtualDesktop;
    private readonly ShellRefreshService _shellRefresh;
    private readonly IconLayoutWorkerClientService _iconLayouts;
    private readonly VirtualDesktopNavigatorService _navigator;
    private readonly KeyboardInputService _keyboard;
    private readonly WallpaperService _wallpapers;
    private readonly FileLogger _logger;

    private RealmConfig? _config;
    private Guid? _lastDesktopId;
    private string _lastMessage = "Initialized.";
    private DateTimeOffset _lastSwitchAt = DateTimeOffset.MinValue;
    private string? _lastDisplayTopologyKey;
    private bool _displayTopologyRestorePending;
    private bool _startupRealmRestorePending = true;
    private bool _iconLayoutsDisabledForSession;
    private string? _iconLayoutsDisabledReason;
    private Guid? _shellReadinessRetryDesktopId;
    private string? _shellReadinessRetryRealmPath;
    private DateTimeOffset _shellReadinessRetryNotBefore = DateTimeOffset.MinValue;
    private int _shellReadinessRetryAttempt;
    private readonly HashSet<Guid> _provisionalDesktopNameSyncWarnings = [];

    private const int MaximumShellReadinessRecoveryAttempts = 3;

    /// <summary>
    /// Raised only after Explorer has explicitly failed the bounded readiness gate. The runtime
    /// schedules the retry through its existing serialized reconciliation lane.
    /// </summary>
    public event Action<TimeSpan>? IconLayoutRecoveryScheduled;

    public DesktopSwitchService(
        RealmConfigService configService,
        KnownFolderService knownFolder,
        VirtualDesktopRegistryService virtualDesktop,
        ShellRefreshService shellRefresh,
        IconLayoutWorkerClientService iconLayouts,
        VirtualDesktopNavigatorService navigator,
        KeyboardInputService keyboard,
        WallpaperService wallpapers,
        FileLogger logger)
    {
        _configService = configService;
        _knownFolder = knownFolder;
        _virtualDesktop = virtualDesktop;
        _shellRefresh = shellRefresh;
        _iconLayouts = iconLayouts;
        _navigator = navigator;
        _keyboard = keyboard;
        _wallpapers = wallpapers;
        _logger = logger;
    }

    public RealmConfig Config => _config ?? throw new InvalidOperationException("Config not initialized.");

    public string IconLayoutRuntimeStatus
    {
        get
        {
            if (_iconLayoutsDisabledForSession)
            {
                return "DISABLED UNTIL RESTART — " + _iconLayoutsDisabledReason;
            }

            if (_shellReadinessRetryDesktopId.HasValue)
            {
                if (_shellReadinessRetryNotBefore == DateTimeOffset.MaxValue)
                {
                    return "EXPLORER RECOVERY PAUSED — bounded retry budget exhausted; use Refresh after Explorer settles.";
                }

                var until = _shellReadinessRetryNotBefore == DateTimeOffset.MinValue
                    ? "pending"
                    : _shellReadinessRetryNotBefore.ToLocalTime().ToString("HH:mm:ss");
                return $"EXPLORER SETTLING — recovery retry {_shellReadinessRetryAttempt}/{MaximumShellReadinessRecoveryAttempts} scheduled ({until})";
            }

            return "Active";
        }
    }

    public void Initialize()
    {
        var currentDesktopPath = _knownFolder.GetDesktopPath();
        _config = _configService.LoadOrCreate(currentDesktopPath);

        if (Config.IconLayoutDisplayTopologyGuardEnabled)
        {
            var topology = DisplayTopologyService.Capture();
            _lastDisplayTopologyKey = topology.Key;
            _logger.Info($"Initial display topology captured: {topology.Key} ({topology.Screens.Count} screen(s), virtual={topology.VirtualBoundsWidth}x{topology.VirtualBoundsHeight}).");
        }

        if (Config.RejectOneDriveDesktop && ContainsOneDriveSegment(Config.OriginalDesktopPath!))
        {
            throw new InvalidOperationException(
                "Original Desktop detected under OneDrive. DeskRealm rejects this mode by default. " +
                "Disable rejectOneDriveDesktop only if you explicitly want to assume this risk.");
        }

        Directory.CreateDirectory(Config.RealmsRoot!);

        // Configuration migrations that bind number-based legacy data to Windows GUIDs
        // must complete before any realm path mutation can save the config.
        MigrateConfigV12RealmMetadata();
        MigrateConfigV13RealmStudioMetadata();
        MigrateConfigV14AdaptiveRecoveryDefaults();
        MigrateConfigV15RealmRenameApplyMode();
        MigrateConfigV16CanonicalRealmModel();
        MigrateConfigV17DirectControlDefaults();
        MigrateConfigV18StartupVisibility();

        // A desktop can be removed through Win+Tab while DeskRealm is not running. Retire only
        // its GUID-bound configuration metadata before the active realm map is reconciled. This
        // never moves folders or layouts; it prevents an orphaned assignment from impersonating
        // a live "Desktop N" fallback while Explorer is rebuilding its metadata.
        var liveDesktops = _virtualDesktop.GetVirtualDesktops();
        RetireStaleRealmMetadata(liveDesktops);
        SyncAllRealmFolderNames(createIfMissing: true, reswitchCurrentDesktop: false);
        EnsureSingleDefaultRealm(liveDesktops);

        _lastMessage = "Config loaded.";
        _logger.Info($"Original Desktop: {Config.OriginalDesktopPath}");
        _logger.Info($"Realms root: {Config.RealmsRoot}");
        _logger.Info("Realm names follow Win+Tab and native wallpapers are always applied for configured realms.");
        _logger.Info($"Icon layout persistence: {Config.IconLayoutPersistenceEnabled}");
        _logger.Info("Icon layouts are saved on confirmed DeskRealm hotkey transitions, manual save, lock merge and exit restore; legacy periodic polling is retired.");
        _logger.Info($"Icon layout worker timeout: {Config.IconLayoutWorkerTimeoutMs} ms");
        _logger.Info($"Adaptive Shell readiness timeout: {Config.ShellViewReadyTimeoutMs} ms");
        _logger.Info($"Adaptive icon verification timeout: {Config.IconLayoutRestoreVerificationTimeoutMs} ms");
        _logger.Info("Startup layout recovery: the first matching realm is restored once even when the Desktop Known Folder already targets it.");
        _logger.Info("Explorer readiness timeout is a visible bounded recovery state: automatic layout retry is scheduled without disabling the session; worker/protocol/config failures remain strict.");
        _logger.Info($"Realm hotkeys: {Config.DesktopHotkeysEnabled} / {string.Join(", ", Config.RealmHotkeys.Select(p => $"{p.Key}={p.Value}"))}");
        _logger.Info(
            $"Hotkey adaptive timing: modifierReleaseTimeout={Config.HotkeyModifierReleaseTimeoutMs} ms, " +
            $"stepConfirmationTimeout={Config.DesktopStepConfirmationTimeoutMs} ms");
    }

    public void Tick()
    {
        if (_config is null)
        {
            Initialize();
        }

        if (!Config.Enabled)
        {
            _lastMessage = "DeskRealm paused.";
            return;
        }

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        var realmPath = ResolveRealmPath(current, createIfMissing: true);
        var currentKnownDesktop = _knownFolder.GetDesktopPath();
        TrackDisplayTopology("tick");

        if (TryRunScheduledShellReadinessRecovery(current, realmPath))
        {
            return;
        }

        if (_lastDesktopId.HasValue &&
            _lastDesktopId.Value == current.Id &&
            string.Equals(currentKnownDesktop, realmPath, StringComparison.OrdinalIgnoreCase))
        {
            RestoreIconLayoutAfterDisplayTopologyChange(current, realmPath);
            return;
        }

        SwitchTo(current, realmPath);
    }

    public void SwitchNow()
    {
        if (_config is null)
        {
            Initialize();
        }

        EnsureDeskRealmEnabledForOperation("Refresh now");

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        var realmPath = ResolveRealmPath(current, createIfMissing: true);
        SwitchTo(current, realmPath, force: true);
    }

    /// <summary>
    /// Switches by the Windows virtual-desktop GUID. This is the canonical Realm Studio
    /// route: UI cards and direct realm hotkeys do not depend on mutable Win+Tab order.
    /// </summary>
    public void SwitchToDesktop(Guid targetDesktopId)
    {
        if (_config is null)
        {
            Initialize();
        }

        if (targetDesktopId == Guid.Empty)
        {
            throw new InvalidOperationException("Cannot switch to an empty Windows virtual-desktop GUID.");
        }

        EnsureDeskRealmEnabledForOperation($"realm switch to {targetDesktopId:B}");

        var desktops = _virtualDesktop.GetVirtualDesktops();
        var target = desktops.FirstOrDefault(d => d.Id == targetDesktopId)
            ?? throw new InvalidOperationException($"Virtual desktop {targetDesktopId:B} no longer exists. DeskRealm will never retarget a hotkey by name or position.");

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        if (current.Id == target.Id)
        {
            var currentRealmPath = ResolveRealmPath(current, createIfMissing: true);
            SwitchTo(current, currentRealmPath, force: false);
            _lastMessage = $"Realm switch ignored: already on desktop #{target.Number} {target.Name}.";
            _logger.Info(_lastMessage);
            return;
        }

        var operation = Stopwatch.StartNew();
        var targetRealmPath = ResolveRealmPath(target, createIfMissing: true);
        _logger.Info(
            $"[PERF] realm GUID preflight complete: target=#{target.Number} {target.Id:B}, realm={targetRealmPath}, " +
            $"elapsed={operation.Elapsed.TotalMilliseconds:0.0} ms.");

        SaveIconLayoutForKnownDesktopIfRealm(_knownFolder.GetDesktopPath(), "realm-guid-pre-navigation-save");
        _keyboard.WaitForNavigationModifiersReleased(Config.HotkeyModifierReleaseTimeoutMs);

        _logger.Info(
            $"Realm GUID parallel transaction starting: source=#{current.Number} {current.Id:B}, " +
            $"target=#{target.Number} {target.Id:B}, realm={targetRealmPath}.");

        using var startGate = new ManualResetEventSlim(false);
        var navigationTask = Task.Run(() =>
        {
            startGate.Wait();
            return _navigator.NavigateByNumber(current, target, desktops, Config.DesktopStepConfirmationTimeoutMs);
        });
        var targetPreparationTask = Task.Run(() =>
        {
            startGate.Wait();
            return ApplyRealmTargetWithoutSourceSave(target, targetRealmPath, "realm-guid-parallel-target");
        });

        var parallelWatch = Stopwatch.StartNew();
        startGate.Set();
        try
        {
            Task.WhenAll(navigationTask, targetPreparationTask).GetAwaiter().GetResult();
        }
        catch
        {
            // The final GUID barrier below observes both bounded branches and reconciles explicitly.
        }
        parallelWatch.Stop();

        var navigationError = GetTaskFailure(navigationTask);
        var preparationError = GetTaskFailure(targetPreparationTask);
        var preparationReady = targetPreparationTask.Status == TaskStatus.RanToCompletion && targetPreparationTask.Result;
        var actual = _virtualDesktop.GetCurrentVirtualDesktop();

        _logger.Info(
            $"[PERF] realm GUID barrier reached: expected=#{target.Number} {target.Id:B}, " +
            $"actual=#{actual.Number} {actual.Id:B}, navigationCompleted={navigationTask.Status == TaskStatus.RanToCompletion}, " +
            $"targetPrepared={preparationReady}, elapsed={parallelWatch.Elapsed.TotalMilliseconds:0.0} ms.");

        if (actual.Id == target.Id)
        {
            if (!preparationReady)
            {
                var speculativeFailure = preparationError?.Message ?? "target preparation returned a degraded result";
                _logger.Warn($"Realm target GUID confirmed but parallel target preparation did not complete cleanly: {speculativeFailure}. Performing final reconciliation.");
                preparationReady = ApplyRealmTargetWithoutSourceSave(target, targetRealmPath, "realm-guid-final-target-reconcile");
            }

            CommitRealmState(target);
            operation.Stop();
            if (!preparationReady && !IsShellReadinessRecoveryPendingFor(target.Id))
            {
                throw new InvalidOperationException(
                    $"Windows reached desktop #{target.Number} {target.Name}, but its target realm layout could not be restored. " +
                    $"Icon layout persistence is disabled for this session. Reason: {_iconLayoutsDisabledReason}");
            }

            if (!preparationReady)
            {
                _logger.Warn(
                    $"Windows reached desktop #{target.Number} {target.Name}; Explorer is still settling its target view. " +
                    "DeskRealm kept the realm switch and scheduled an explicit layout recovery retry.");
            }

            if (navigationError is not null)
            {
                _logger.Warn($"Realm navigation reported an intermediate confirmation error, but the final GUID barrier confirmed the requested target: {navigationError.Message}");
            }

            _lastMessage = preparationReady
                ? $"Realm switch -> desktop #{target.Number} {target.Name}."
                : $"Realm switch -> desktop #{target.Number} {target.Name}; Explorer layout recovery is pending.";
            _logger.Info(_lastMessage);
            _logger.Info($"[PERF] realm GUID switch complete: target=#{target.Number}, elapsed={operation.Elapsed.TotalMilliseconds:0.0} ms.");
            return;
        }

        var actualRealmPath = ResolveRealmPath(actual, createIfMissing: true);
        _logger.Warn(
            $"Realm GUID navigation mismatch: expected desktop #{target.Number} {target.Id:B}, " +
            $"but Windows confirmed #{actual.Number} {actual.Id:B}. Discarding prepared target realm and compensating to {actualRealmPath}.");

        var compensationReady = ApplyRealmTargetWithoutSourceSave(actual, actualRealmPath, "realm-guid-navigation-mismatch-compensation");
        CommitRealmState(actual);
        operation.Stop();
        _lastMessage = compensationReady
            ? $"Realm GUID mismatch compensated to desktop #{actual.Number} {actual.Name}."
            : IsShellReadinessRecoveryPendingFor(actual.Id)
                ? $"Realm GUID mismatch reached desktop #{actual.Number} {actual.Name}; Explorer layout recovery is pending."
                : $"Realm GUID mismatch reached desktop #{actual.Number} {actual.Name}; realm selected but icon persistence is disabled.";
        _logger.Warn(_lastMessage);

        var navigationDetail = navigationError is null ? "no navigation exception" : navigationError.Message;
        var preparationDetail = preparationError is null ? "target preparation completed" : preparationError.Message;
        var compensationDetail = compensationReady
            ? "the actual desktop realm was restored and verified"
            : IsShellReadinessRecoveryPendingFor(actual.Id)
                ? "the actual desktop realm is active and Explorer readiness recovery was explicitly scheduled"
                : "the actual desktop folder was selected, but icon persistence is disabled for this session";

        if (compensationReady)
        {
            _logger.Warn(
                $"Realm GUID navigation did not reach desktop #{target.Number} {target.Name}. " +
                $"Windows ended on #{actual.Number} {actual.Name}; {compensationDetail}. " +
                $"Navigation: {navigationDetail}. No blocking recovery action is needed.");
            return;
        }

        if (IsShellReadinessRecoveryPendingFor(actual.Id))
        {
            _logger.Warn(
                $"Realm GUID navigation ended on #{actual.Number} {actual.Name}; {compensationDetail}. " +
                $"Navigation: {navigationDetail}. The serialized Explorer recovery retry remains pending.");
            return;
        }

        throw new InvalidOperationException(
            $"DeskRealm realm transaction did not reach desktop #{target.Number} {target.Name}. " +
            $"Windows ended on desktop #{actual.Number} {actual.Name}; {compensationDetail}. " +
            $"Navigation: {navigationDetail}. Target preparation: {preparationDetail}.");
    }

    public RealmProfile GetRealmProfile(Guid desktopId)
    {
        EnsureInitialized();
        var key = ToConfigGuidKey(desktopId);
        if (!Config.RealmProfiles.TryGetValue(key, out var profile))
        {
            return new RealmProfile();
        }

        return new RealmProfile
        {
            IsFavorite = profile.IsFavorite,
            ActivateOnDeskRealmStartup = profile.ActivateOnDeskRealmStartup
        };
    }

    public string? GetRealmHotkey(Guid desktopId)
    {
        EnsureInitialized();
        return Config.RealmHotkeys.TryGetValue(ToConfigGuidKey(desktopId), out var hotkey) ? hotkey : null;
    }

    public RealmWallpaper? GetRealmWallpaper(Guid desktopId)
    {
        EnsureInitialized();
        if (!Config.RealmWallpapers.TryGetValue(ToConfigGuidKey(desktopId), out var wallpaper)) return null;
        return new RealmWallpaper
        {
            ManagedPath = wallpaper.ManagedPath,
            SourceFileName = wallpaper.SourceFileName,
            UpdatedAt = wallpaper.UpdatedAt
        };
    }

    /// <summary>
    /// Reconciles the wallpaper Windows currently records for this virtual-desktop GUID.
    /// Importing is one-way Windows → managed DeskRealm asset; this inspection never
    /// applies a wallpaper, changes the active desktop or restarts Explorer.
    /// </summary>
    public RealmWallpaperSyncResult SynchronizeRealmWallpaperFromWindows(Guid desktopId)
    {
        EnsureInitialized();
        _ = FindVirtualDesktopOrThrow(desktopId);
        var configKey = ToConfigGuidKey(desktopId);
        Config.RealmWallpapers.TryGetValue(configKey, out var existing);
        var nativePath = _wallpapers.TryGetNativeAssignment(desktopId);

        if (string.IsNullOrWhiteSpace(nativePath))
        {
            if (existing is not null && File.Exists(existing.ManagedPath))
            {
                return new RealmWallpaperSyncResult(
                    existing,
                    existing.ManagedPath,
                    string.IsNullOrWhiteSpace(existing.SourceFileName) ? Path.GetFileName(existing.ManagedPath) : existing.SourceFileName,
                    "DeskRealm wallpaper metadata is available; Windows did not expose a per-desktop Registry value during this refresh.",
                    true,
                    false);
            }

            return RealmWallpaperSyncResult.NoWallpaper;
        }

        if (!File.Exists(nativePath))
        {
            _logger.Warn($"Windows wallpaper Registry value is unreadable for {desktopId:B}: '{nativePath}'. DeskRealm will not replace it silently.");
            return new RealmWallpaperSyncResult(
                existing,
                string.Empty,
                Path.GetFileName(nativePath),
                "Windows wallpaper detected — preview unavailable because the referenced file cannot be read.",
                false,
                false);
        }

        if (existing is not null && File.Exists(existing.ManagedPath) && _wallpapers.RefersToSameImage(existing.ManagedPath, nativePath))
        {
            return new RealmWallpaperSyncResult(
                existing,
                existing.ManagedPath,
                string.IsNullOrWhiteSpace(existing.SourceFileName) ? Path.GetFileName(nativePath) : existing.SourceFileName,
                "Windows wallpaper is synchronized with DeskRealm.",
                true,
                false);
        }

        var imported = _wallpapers.ImportManagedCopy(desktopId, nativePath);
        Config.RealmWallpapers[configKey] = imported;
        _configService.Save(Config);
        _lastMessage = $"Windows wallpaper imported into DeskRealm for {desktopId:B}.";
        _logger.Info($"{_lastMessage} native='{nativePath}', managed='{imported.ManagedPath}'.");
        return new RealmWallpaperSyncResult(
            imported,
            imported.ManagedPath,
            imported.SourceFileName,
            "Windows wallpaper imported into DeskRealm and preview refreshed.",
            true,
            true);
    }

    public void SetRealmWallpaper(Guid desktopId, string sourcePath)
    {
        EnsureInitialized();
        _ = FindVirtualDesktopOrThrow(desktopId);
        var wallpaper = _wallpapers.ImportManagedCopy(desktopId, sourcePath);
        _wallpapers.PersistNativeAssignment(desktopId, wallpaper);
        Config.RealmWallpapers[ToConfigGuidKey(desktopId)] = wallpaper;
        _configService.Save(Config);

        if (_virtualDesktop.GetCurrentVirtualDesktop().Id == desktopId)
        {
            _wallpapers.ApplyForActiveDesktop(desktopId, wallpaper);
        }

        _lastMessage = $"Realm wallpaper saved for {desktopId:B}.";
        _logger.Info(_lastMessage);
    }

    public void ClearRealmWallpaper(Guid desktopId)
    {
        EnsureInitialized();
        _ = FindVirtualDesktopOrThrow(desktopId);
        Config.RealmWallpapers.Remove(ToConfigGuidKey(desktopId));
        _wallpapers.ClearNativeAssignment(desktopId);
        _configService.Save(Config);
        _lastMessage = $"DeskRealm wallpaper assignment removed for {desktopId:B}. Windows keeps the currently visible wallpaper until you choose another one.";
        _logger.Info(_lastMessage);
    }

    /// <summary>
    /// True only for the Windows Known Folder explicitly captured as DeskRealm's original
    /// Desktop during first-run setup. Other absolute assignments remain external and strict.
    /// </summary>
    public bool IsNativeDesktopRealm(Guid desktopId)
    {
        EnsureInitialized();
        var key = desktopId.ToString("B");
        return Config.Assignments.TryGetValue(key, out var assignment) &&
               IsOriginalNativeDesktopAssignment(assignment);
    }

    public void UpdateRealmProfile(Guid desktopId, bool isFavorite, bool activateOnDeskRealmStartup, string? hotkey)
    {
        EnsureInitialized();
        if (!_virtualDesktop.GetVirtualDesktops().Any(desktop => desktop.Id == desktopId))
        {
            throw new InvalidOperationException($"Cannot update profile for missing Windows virtual desktop {desktopId:B}.");
        }

        var key = ToConfigGuidKey(desktopId);
        // Validate every user input before mutating the in-memory configuration. A failed
        // hotkey save must never silently clear the previous mapping or profile flags.
        var normalizedHotkey = string.IsNullOrWhiteSpace(hotkey) ? null : HotkeyParser.Parse(desktopId, hotkey.Trim()).Text;
        if (normalizedHotkey is not null)
        {
            var duplicate = Config.RealmHotkeys.FirstOrDefault(pair =>
                !string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pair.Value, normalizedHotkey, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(duplicate.Key))
            {
                throw new InvalidOperationException($"Duplicate realm hotkey: {normalizedHotkey}. It is already assigned to {duplicate.Key}.");
            }
        }

        // Realm Studio v0.7.0 treats the star as the single default-realm contract.
        // The legacy ActivateOnDeskRealmStartup field is mirrored for migration/audit,
        // but runtime startup selects only the unique default realm.
        if (isFavorite || activateOnDeskRealmStartup)
        {
            foreach (var profile in Config.RealmProfiles.Values)
            {
                profile.IsFavorite = false;
                profile.ActivateOnDeskRealmStartup = false;
            }
        }

        var isDefault = isFavorite || activateOnDeskRealmStartup;
        Config.RealmProfiles[key] = new RealmProfile
        {
            IsFavorite = isDefault,
            ActivateOnDeskRealmStartup = isDefault
        };
        Config.RealmHotkeys.Remove(key);
        if (normalizedHotkey is not null)
        {
            Config.RealmHotkeys[key] = normalizedHotkey;
        }

        _configService.Save(Config);
        _lastMessage = $"Realm profile saved for {desktopId:B}.";
        _logger.Info(_lastMessage);
    }

    public void SetDefaultRealm(Guid desktopId)
    {
        EnsureInitialized();
        _ = FindVirtualDesktopOrThrow(desktopId);
        var profile = GetRealmProfile(desktopId);
        if (profile.IsFavorite)
        {
            _lastMessage = $"Realm already selected as default: {desktopId:B}.";
            _logger.Info(_lastMessage);
            return;
        }

        UpdateRealmProfile(desktopId, isFavorite: true, activateOnDeskRealmStartup: true, GetRealmHotkey(desktopId));
        _lastMessage = $"Default realm selected: {desktopId:B}.";
        _logger.Info(_lastMessage);
    }

    public void ToggleRealmLock(Guid desktopId)
    {
        EnsureInitialized();
        if (IsRealmLocked(desktopId))
        {
            UnlockRealmLayoutsForDesktop(desktopId);
            return;
        }

        LockRealmLayoutsForDesktop(desktopId);
    }

    public void UpdateRealmHotkey(Guid desktopId, string? hotkey)
    {
        EnsureInitialized();
        var profile = GetRealmProfile(desktopId);
        UpdateRealmProfile(desktopId, profile.IsFavorite, profile.ActivateOnDeskRealmStartup, hotkey);
    }

    public bool TryGetDefaultRealm(out Guid desktopId)
    {
        EnsureInitialized();
        var item = Config.RealmProfiles.FirstOrDefault(pair => pair.Value.IsFavorite && Guid.TryParse(pair.Key, out _));
        if (string.IsNullOrWhiteSpace(item.Key) || !Guid.TryParse(item.Key, out desktopId))
        {
            desktopId = Guid.Empty;
            return false;
        }
        return true;
    }

    public void SwitchToDefaultRealmIfConfigured()
    {
        EnsureInitialized();
        if (!Config.Enabled)
        {
            _logger.Info("Default realm startup switch skipped: DeskRealm automation is disabled.");
            return;
        }
        if (!TryGetDefaultRealm(out var desktopId)) return;
        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        if (current.Id == desktopId)
        {
            _logger.Info($"DeskRealm default realm already active at startup: {desktopId:B}.");
            return;
        }

        _logger.Info($"Switching to configured default realm at startup: {desktopId:B}.");
        SwitchToDesktop(desktopId);
    }

    public VirtualDesktopInfo CreateVirtualDesktop()
    {
        EnsureInitialized();
        EnsureDeskRealmEnabledForOperation("create Windows virtual desktop");
        var before = _virtualDesktop.GetVirtualDesktops().ToList();
        _keyboard.WaitForNavigationModifiersReleased(Config.HotkeyModifierReleaseTimeoutMs);
        _keyboard.CreateVirtualDesktop();
        var created = _navigator.WaitForNewDesktop(before.Select(item => item.Id).ToHashSet(), Config.DesktopStepConfirmationTimeoutMs);
        var realmPath = ResolveRealmPath(created, createIfMissing: true);
        SwitchTo(created, realmPath, force: true);
        _lastMessage = $"Windows virtual desktop created: #{created.Number} {created.Name} {created.Id:B}.";
        _logger.Info(_lastMessage);
        return created;
    }

    public void DeleteVirtualDesktop(Guid desktopId)
    {
        EnsureInitialized();
        EnsureDeskRealmEnabledForOperation("delete Windows virtual desktop");
        var desktops = _virtualDesktop.GetVirtualDesktops().OrderBy(item => item.Number).ToList();
        if (desktops.Count <= 1)
        {
            throw new InvalidOperationException("Windows refuses to close the last virtual desktop. Create another desktop first.");
        }

        var target = desktops.FirstOrDefault(item => item.Id == desktopId)
            ?? throw new InvalidOperationException($"Cannot delete missing Windows virtual desktop {desktopId:B}.");
        var profile = GetRealmProfile(desktopId);
        if (profile.IsFavorite)
        {
            throw new InvalidOperationException("The default realm cannot be deleted. Choose another default realm first.");
        }
        if (IsRealmLocked(desktopId))
        {
            throw new InvalidOperationException("This realm is locked. Unlock the realm before deleting its Windows virtual desktop.");
        }
        if (IsLayoutLocked(desktopId) || IsCurrentVariantLocked(desktopId))
        {
            throw new InvalidOperationException("This realm has a protected layout/variant. Unlock it before deleting the desktop so deletion remains explicit.");
        }

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        if (current.Id != target.Id)
        {
            SwitchToDesktop(target.Id);
        }

        _keyboard.WaitForNavigationModifiersReleased(Config.HotkeyModifierReleaseTimeoutMs);
        _keyboard.CloseCurrentVirtualDesktop();
        var remainingCurrent = _navigator.WaitForDesktopRemoval(target.Id, Config.DesktopStepConfirmationTimeoutMs);
        ArchiveAndRemoveDeletedRealmMetadata(target);
        var remainingPath = ResolveRealmPath(remainingCurrent, createIfMissing: true);
        SwitchTo(remainingCurrent, remainingPath, force: true);
        _lastMessage = $"Windows virtual desktop deleted: #{target.Number} {target.Name} {target.Id:B}.";
        _logger.Info(_lastMessage);
    }

    /// <summary>
    /// Returns the single source-of-truth rename availability used by Realm Studio before
    /// user confirmation and again by RenameRealm before any mutation. Live desktop
    /// collisions are never downgraded to an archived-profile decision.
    /// </summary>
    public RealmNameAvailability GetRealmNameAvailability(Guid desktopId, string displayName)
    {
        EnsureInitialized();
        var desktop = FindVirtualDesktopOrThrow(desktopId);
        var name = displayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("A realm name is required.");
        }

        var folderName = RealmFolderNameSanitizer.FromVirtualDesktopName(name, Config.RealmNameMaxLength);
        if (string.Equals(desktop.Name, name, StringComparison.Ordinal))
        {
            return RealmNameAvailability.Unchanged(desktopId, name, folderName);
        }

        var desktops = _virtualDesktop.GetVirtualDesktops();
        var activeConflict = desktops.FirstOrDefault(candidate =>
            candidate.Id != desktopId &&
            (string.Equals(candidate.Name.Trim(), name, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(RealmFolderNameSanitizer.FromVirtualDesktopName(candidate.Name, Config.RealmNameMaxLength), folderName, StringComparison.OrdinalIgnoreCase)));

        if (activeConflict is null)
        {
            var liveDesktopIds = desktops.Select(candidate => candidate.Id).ToHashSet();
            var assignmentConflict = Config.Assignments.FirstOrDefault(pair =>
                !string.Equals(pair.Key, desktopId.ToString("B"), StringComparison.OrdinalIgnoreCase) &&
                IsAssignmentOwnedByLiveDesktop(pair.Key, liveDesktopIds) &&
                !IsAbsoluteRealmAssignment(pair.Value) &&
                string.Equals(pair.Value, folderName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(assignmentConflict.Key) && Guid.TryParse(assignmentConflict.Key, out var assignmentDesktopId))
            {
                activeConflict = desktops.FirstOrDefault(candidate => candidate.Id == assignmentDesktopId);
            }
        }

        if (activeConflict is not null)
        {
            return RealmNameAvailability.LiveConflict(desktopId, name, folderName, activeConflict);
        }

        Config.ArchivedRealmProfiles.TryGetValue(folderName, out var archived);
        return RealmNameAvailability.Available(desktopId, name, folderName, archived);
    }

    public void RenameRealm(Guid desktopId, string displayName, RealmDuplicateResolution duplicateResolution)
    {
        EnsureInitialized();
        var desktop = FindVirtualDesktopOrThrow(desktopId);
        var name = string.IsNullOrWhiteSpace(displayName)
            ? throw new InvalidOperationException("Realm name cannot be empty.")
            : displayName.Trim();
        var nameAvailability = GetRealmNameAvailability(desktopId, name);
        if (nameAvailability.HasActiveConflict && nameAvailability.ActiveConflict is not null)
        {
            var conflict = nameAvailability.ActiveConflict;
            throw new InvalidOperationException(
                $"The name '{name}' is already used by active desktop #{conflict.Number} '{conflict.Name}'. " +
                "DeskRealm will not merge two live desktops. Choose a distinct name first.");
        }

        var assignmentKey = desktopId.ToString("B");
        var currentPath = ResolveRealmPath(desktop, createIfMissing: true);
        var assignment = Config.Assignments[assignmentKey];

        // The original Windows Desktop is deliberately represented by its absolute Known Folder
        // path after the explicit first-run association. It is not an arbitrary external realm:
        // Realm Studio may rename the *virtual desktop label*, but it must never move, rename,
        // remap, or otherwise mutate the native Desktop folder itself.
        if (IsAbsoluteRealmAssignment(assignment))
        {
            if (!IsOriginalNativeDesktopAssignment(assignment))
            {
                throw new InvalidOperationException("This realm points to an external Desktop path and cannot be renamed from Realm Studio.");
            }

            if (!string.Equals(desktop.Name, name, StringComparison.Ordinal))
            {
                _virtualDesktop.SetDesktopName(desktopId, name);
                _lastMessage = $"Native Desktop realm label persisted: {desktop.Name} -> {name}. The original Windows Desktop folder assignment was preserved unchanged.";
                _logger.Info(_lastMessage + $" desktop={desktopId:B}, nativePath='{assignment}'.");
            }
            else
            {
                _lastMessage = $"Native Desktop realm display name unchanged: {name}. The original Windows Desktop folder assignment was preserved unchanged.";
                _logger.Info(_lastMessage + $" desktop={desktopId:B}, nativePath='{assignment}'.");
            }

            return;
        }

        var folderName = RealmFolderNameSanitizer.FromVirtualDesktopName(name, Config.RealmNameMaxLength);
        var currentFolder = assignment;
        var targetPath = Path.Combine(Config.RealmsRoot!, folderName);
        var pathsAlreadyMatch = PathsEqual(currentPath, targetPath);
        var archived = nameAvailability.ArchivedProfile;
        var targetExists = Directory.Exists(targetPath) && !pathsAlreadyMatch;
        if (targetExists && archived is null)
        {
            throw new InvalidOperationException(
                $"A non-archived realm folder already exists for '{name}': {targetPath}. Resolve it manually rather than allowing DeskRealm to overwrite unknown files.");
        }

        if (archived is not null)
        {
            if (duplicateResolution == RealmDuplicateResolution.Ask)
            {
                throw new InvalidOperationException(
                    $"An archived realm named '{name}' requires an explicit reuse decision. Choose 'Reuse archived layout' or 'Start fresh' before saving. Nothing changed.");
            }

            if (duplicateResolution == RealmDuplicateResolution.ReuseArchivedLayout)
            {
                if (!Guid.TryParse(archived.SourceDesktopId, out var sourceDesktopId))
                {
                    throw new InvalidOperationException($"Archived realm '{name}' has an invalid source desktop GUID.");
                }
                IconLayoutPersistenceService.CopyLayoutForDesktop(sourceDesktopId, desktopId, folderName, overwriteTarget: true);
                if (archived.Wallpaper is not null && File.Exists(archived.Wallpaper.ManagedPath))
                {
                    Config.RealmWallpapers[ToConfigGuidKey(desktopId)] = archived.Wallpaper;
                    _wallpapers.PersistNativeAssignment(desktopId, archived.Wallpaper);
                }
                _logger.Info($"Archived realm layout explicitly reused: name='{folderName}', source={sourceDesktopId:B}, target={desktopId:B}.");
            }
            else if (duplicateResolution == RealmDuplicateResolution.OverwriteArchivedLayout)
            {
                IconLayoutPersistenceService.DeleteLayoutForDesktop(desktopId);
                _logger.Info($"Archived realm layout explicitly overwritten with a fresh target layout: name='{folderName}', target={desktopId:B}.");
            }
        }

        if (!pathsAlreadyMatch)
        {
            TemporarilyRestoreDesktopIfActiveRealm(currentPath, folderName);
            if (targetExists)
            {
                if (Directory.Exists(currentPath))
                {
                    var currentEntries = Directory.EnumerateFileSystemEntries(currentPath).Take(1).ToList();
                    if (currentEntries.Count > 0)
                    {
                        var archiveFolder = Path.Combine(Config.RealmsRoot!, $"_DeskRealmPreserved_{currentFolder}_{DateTimeOffset.Now:yyyyMMddHHmmss}");
                        Directory.Move(currentPath, archiveFolder);
                        _logger.Warn($"Current realm folder preserved during archived-name reuse: {currentPath} -> {archiveFolder}");
                    }
                    else
                    {
                        Directory.Delete(currentPath);
                    }
                }
            }
            else if (Directory.Exists(currentPath))
            {
                Directory.Move(currentPath, targetPath);
            }
            else
            {
                Directory.CreateDirectory(targetPath);
            }

            MoveRealmLockPathKey(currentPath, targetPath);
            Config.Assignments[assignmentKey] = folderName;
        }

        _virtualDesktop.SetDesktopName(desktopId, name);
        _configService.Save(Config);
        var renamedDesktop = new VirtualDesktopInfo(desktopId, name, desktop.Number);
        if (_virtualDesktop.GetCurrentVirtualDesktop().Id == desktopId)
        {
            SwitchTo(renamedDesktop, targetPath, force: true);
        }
        _lastMessage = $"Realm label persisted: {desktop.Name} -> {name}. GUID-bound hotkey/default/wallpaper metadata was retained; realm-path lock keys were migrated.";
        _logger.Info(_lastMessage);
    }

    public void SyncRealmNamesNow()
    {
        if (_config is null)
        {
            Initialize();
        }

        SyncAllRealmFolderNames(createIfMissing: true, reswitchCurrentDesktop: true);
    }

    public IReadOnlyList<IconLayoutRealmSnapshot> GetIconLayoutLockSnapshot()
    {
        EnsureInitialized();

        var currentDesktopId = _virtualDesktop.GetCurrentVirtualDesktop().Id;
        var desktops = _virtualDesktop.GetVirtualDesktops().OrderBy(desktop => desktop.Number).ToList();
        var groups = new Dictionary<string, IconLayoutRealmBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var desktop in desktops)
        {
            var realmPath = ResolveRealmPath(desktop, createIfMissing: true);
            var realmKey = BuildRealmLockKey(realmPath);
            if (!groups.TryGetValue(realmKey, out var builder))
            {
                builder = new IconLayoutRealmBuilder(realmPath, desktop.Number);
                groups.Add(realmKey, builder);
            }

            var layoutLocked = IsLayoutLocked(desktop.Id);
            var realmLocked = IsRealmLocked(desktop.Id);
            var currentTopology = desktop.Id == currentDesktopId ? DisplayTopologyService.Capture().Key : string.Empty;
            var variants = BuildVariantSnapshots(desktop, realmLocked, layoutLocked, currentTopology);
            builder.Layouts.Add(new IconLayoutEntrySnapshot(
                desktop.Id,
                layoutLocked,
                realmLocked || layoutLocked,
                variants.Any(variant => variant.HasSavedLayout),
                variants));
        }

        return groups.Values
            .OrderBy(group => group.FirstDesktopNumber)
            .Select(group => new IconLayoutRealmSnapshot(
                group.RealmPath,
                group.Layouts.Any(layout => IsRealmLocked(layout.DesktopId)),
                group.Layouts))
            .ToList();
    }

    private IReadOnlyList<IconLayoutVariantSnapshot> BuildVariantSnapshots(
        VirtualDesktopInfo desktop,
        bool realmLocked,
        bool layoutLocked,
        string currentTopologyKey)
    {
        var fileVariants = IconLayoutPersistenceService.ReadLayoutVariantsForDesktop(desktop.Id);
        if (fileVariants.Count == 0)
        {
            return
            [
                new IconLayoutVariantSnapshot(
                    "pending-baseline",
                    "No saved icon layout yet",
                    null,
                    0,
                    !string.IsNullOrWhiteSpace(currentTopologyKey),
                    false,
                    realmLocked || layoutLocked,
                    false,
                    realmLocked,
                    layoutLocked)
            ];
        }

        return fileVariants
            .Select(variant =>
            {
                var variantLocked = IsVariantLocked(BuildVariantLockKey(desktop.Id, variant.DisplayTopologyKey));
                return new IconLayoutVariantSnapshot(
                    variant.DisplayTopologyKey,
                    variant.Summary,
                    variant.SavedAt,
                    variant.IconCount,
                    !string.IsNullOrWhiteSpace(currentTopologyKey) && string.Equals(variant.DisplayTopologyKey, currentTopologyKey, StringComparison.OrdinalIgnoreCase),
                    variantLocked,
                    realmLocked || layoutLocked || variantLocked,
                    true,
                    realmLocked,
                    layoutLocked);
            })
            .ToList();
    }

    public void LockIconLayoutVariant(Guid desktopId, string displayTopologyKey)
    {
        if (_config is null)
        {
            Initialize();
        }

        if (string.IsNullOrWhiteSpace(displayTopologyKey) || string.Equals(displayTopologyKey, "pending-baseline", StringComparison.OrdinalIgnoreCase))
        {
            LockIconLayout(desktopId);
            return;
        }

        var desktop = FindVirtualDesktopOrThrow(desktopId);
        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        if (desktop.Id == current.Id && !IsLayoutOrRealmLocked(desktop.Id))
        {
            var active = GetActiveCurrentRealmOrThrow("variant lock");
            SaveCurrentIconLayoutBaseline(active.Desktop, active.RealmName, "variant-lock-baseline");
        }

        var key = BuildVariantLockKey(desktop.Id, displayTopologyKey);
        Config.LockedIconLayoutVariants[key] = true;
        _configService.Save(Config);
        _lastMessage = $"Icon layout variant locked: {desktop.Name}";
        _logger.Info($"{_lastMessage} ({displayTopologyKey})");
    }

    public void UnlockIconLayoutVariant(Guid desktopId, string displayTopologyKey)
    {
        if (_config is null)
        {
            Initialize();
        }

        if (string.IsNullOrWhiteSpace(displayTopologyKey) || string.Equals(displayTopologyKey, "pending-baseline", StringComparison.OrdinalIgnoreCase))
        {
            UnlockIconLayout(desktopId);
            return;
        }

        var desktop = FindVirtualDesktopOrThrow(desktopId);
        var key = BuildVariantLockKey(desktop.Id, displayTopologyKey);
        Config.LockedIconLayoutVariants.Remove(key);
        _configService.Save(Config);
        _lastMessage = $"Icon layout variant unlocked: {desktop.Name}";
        _logger.Info($"{_lastMessage} ({displayTopologyKey})");
    }

    public void DeleteIconLayoutVariant(Guid desktopId, string displayTopologyKey)
    {
        if (_config is null)
        {
            Initialize();
        }

        if (string.IsNullOrWhiteSpace(displayTopologyKey) || string.Equals(displayTopologyKey, "pending-baseline", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cannot delete an icon layout variant that has not been saved yet.");
        }

        var desktop = FindVirtualDesktopOrThrow(desktopId);
        var persistence = new IconLayoutPersistenceService(new DesktopIconShellService(_logger), _knownFolder, _logger);
        persistence.DeleteVariant(desktop.Id, displayTopologyKey);

        Config.LockedIconLayoutVariants.Remove(BuildVariantLockKey(desktop.Id, displayTopologyKey));
        _configService.Save(Config);

        _lastMessage = $"Icon layout variant deleted: {desktop.Name}";
        _logger.Info($"{_lastMessage} ({displayTopologyKey})");
    }

    public void LockIconLayout(Guid desktopId)
    {
        if (_config is null)
        {
            Initialize();
        }

        var desktop = FindVirtualDesktopOrThrow(desktopId);
        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        if (desktop.Id == current.Id && !IsLayoutOrRealmLocked(desktop.Id))
        {
            var active = GetActiveCurrentRealmOrThrow("layout lock");
            SaveCurrentIconLayoutBaseline(active.Desktop, active.RealmName, "layout-lock-baseline");
        }
        else if (desktop.Id != current.Id)
        {
            _logger.Info($"Layout lock marked for non-current desktop; existing saved layout will be protected, and a baseline will be captured on first visit if none exists: {desktop.Name} {desktop.Id:B}.");
        }
        else
        {
            _logger.Info($"Layout lock requested while already protected; baseline not overwritten: {desktop.Name} {desktop.Id:B}.");
        }

        Config.LockedIconLayouts[desktop.Id.ToString("B")] = true;
        _configService.Save(Config);
        _lastMessage = $"Layout locked: {desktop.Name}";
        _logger.Info(_lastMessage);
    }

    public void UnlockIconLayout(Guid desktopId)
    {
        if (_config is null)
        {
            Initialize();
        }

        var desktop = FindVirtualDesktopOrThrow(desktopId);
        Config.LockedIconLayouts.Remove(desktop.Id.ToString("B"));
        _configService.Save(Config);
        _lastMessage = $"Layout unlocked: {desktop.Name}";
        _logger.Info(_lastMessage);
    }

    public void LockRealmLayoutsForDesktop(Guid desktopId)
    {
        if (_config is null)
        {
            Initialize();
        }

        var desktop = FindVirtualDesktopOrThrow(desktopId);
        var realmPath = ResolveRealmPath(desktop, createIfMissing: true);
        var realmKey = BuildRealmLockKey(realmPath);
        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        if (desktop.Id == current.Id && !IsLayoutOrRealmLocked(desktop.Id))
        {
            var active = GetActiveCurrentRealmOrThrow("realm lock");
            SaveCurrentIconLayoutBaseline(active.Desktop, active.RealmName, "realm-lock-baseline");
        }
        else if (desktop.Id != current.Id)
        {
            _logger.Info($"Realm lock marked from non-current desktop; all layouts assigned to this realm are now protected: {realmPath}.");
        }
        else
        {
            _logger.Info($"Realm lock requested while already protected; baseline not overwritten: {realmPath}.");
        }

        Config.LockedRealms[realmKey] = true;
        RemoveLegacyRealmLockGuidsForRealm(realmKey);
        _configService.Save(Config);
        _lastMessage = $"Realm locked: {Path.GetFileName(realmPath)}";
        _logger.Info(_lastMessage);
    }

    public void UnlockRealmLayoutsForDesktop(Guid desktopId)
    {
        if (_config is null)
        {
            Initialize();
        }

        var desktop = FindVirtualDesktopOrThrow(desktopId);
        var realmPath = ResolveRealmPath(desktop, createIfMissing: true);
        var realmKey = BuildRealmLockKey(realmPath);
        Config.LockedRealms.Remove(realmKey);
        Config.LockedRealms.Remove(desktop.Id.ToString("B"));
        RemoveLegacyRealmLockGuidsForRealm(realmKey);
        _configService.Save(Config);
        _lastMessage = $"Realm unlocked: {Path.GetFileName(realmPath)}";
        _logger.Info(_lastMessage);
    }




    public bool ShouldOfferInitialDesktopImport()
    {
        if (_config is null)
        {
            Initialize();
        }

        return Config.InitialDesktopImportPromptEnabled &&
               !Config.InitialDesktopImportPromptCompleted &&
               !string.IsNullOrWhiteSpace(Config.OriginalDesktopPath) &&
               Directory.Exists(Config.OriginalDesktopPath) &&
               PathsEqual(_knownFolder.GetDesktopPath(), Config.OriginalDesktopPath);
    }

    public IReadOnlyList<VirtualDesktopInfo> GetVirtualDesktopsSnapshot()
    {
        if (_config is null)
        {
            Initialize();
        }

        return _virtualDesktop.GetVirtualDesktops();
    }

    public Guid GetCurrentVirtualDesktopId()
    {
        if (_config is null)
        {
            Initialize();
        }

        return _virtualDesktop.GetCurrentVirtualDesktop().Id;
    }

    public int SkipInitialDesktopImportAndCreateOriginalDesktopShortcuts()
    {
        if (_config is null)
        {
            Initialize();
        }

        var created = CreateOriginalDesktopShortcutsInManagedRealms();
        Config.InitialDesktopImportPromptCompleted = true;
        _configService.Save(Config);
        _lastMessage = $"Initial Desktop import skipped. Original Desktop shortcuts created: {created}.";
        _logger.Info(_lastMessage);
        return created;
    }

    public int CreateOriginalDesktopShortcutsInManagedRealms()
    {
        if (_config is null)
        {
            Initialize();
        }

        var originalDesktop = Config.OriginalDesktopPath
            ?? throw new InvalidOperationException("originalDesktopPath is missing from config.");

        if (!Directory.Exists(originalDesktop))
        {
            throw new DirectoryNotFoundException($"Original Desktop not found: {originalDesktop}");
        }

        var realmsRoot = Config.RealmsRoot
            ?? throw new InvalidOperationException("realmsRoot is missing from config.");

        Directory.CreateDirectory(realmsRoot);

        var created = 0;
        var desktops = _virtualDesktop.GetVirtualDesktops();
        foreach (var desktop in desktops.OrderBy(d => d.Number))
        {
            var realmPath = ResolveRealmPath(desktop, createIfMissing: true);
            if (PathsEqual(realmPath, originalDesktop))
            {
                _logger.Info($"Original Desktop shortcut skipped for {desktop.Name}: realm is the original Desktop ({realmPath}).");
                continue;
            }

            if (!IsPathInsideOrEqual(realmPath, realmsRoot))
            {
                _logger.Info($"Original Desktop shortcut skipped for {desktop.Name}: realm is external to DeskRealm root ({realmPath}).");
                continue;
            }

            Directory.CreateDirectory(realmPath);
            var shortcutPath = Path.Combine(realmPath, "DeskRealm - Original Desktop.lnk");
            CreateFolderShortcut(shortcutPath, originalDesktop, "Open the original Windows Desktop captured before DeskRealm switching.");
            created++;
            _logger.Info($"Original Desktop shortcut created: {shortcutPath} -> {originalDesktop}");
        }

        if (created == 0)
        {
            throw new InvalidOperationException("No original Desktop shortcut was created: no DeskRealm-managed realm was available.");
        }

        _lastMessage = $"Original Desktop shortcuts created: {created}.";
        return created;
    }

    public void ImportOriginalDesktopToVirtualDesktop(Guid targetDesktopId, bool saveLayout)
    {
        if (_config is null)
        {
            Initialize();
        }

        var originalDesktop = Config.OriginalDesktopPath
            ?? throw new InvalidOperationException("originalDesktopPath is missing from config.");

        if (!Directory.Exists(originalDesktop))
        {
            throw new DirectoryNotFoundException($"Original Desktop not found: {originalDesktop}");
        }

        var knownDesktop = _knownFolder.GetDesktopPath();
        if (!PathsEqual(knownDesktop, originalDesktop))
        {
            throw new InvalidOperationException(
                "Initial Desktop import refused: the active known Desktop is no longer the original Desktop. " +
                $"Expected: {originalDesktop}. Current: {knownDesktop}.");
        }

        var targetDesktop = _virtualDesktop.GetVirtualDesktops().FirstOrDefault(d => d.Id == targetDesktopId)
            ?? throw new InvalidOperationException($"Target virtual desktop not found: {targetDesktopId:B}");

        EnsureOriginalDesktopNotAssignedToAnotherDesktop(targetDesktop.Id, originalDesktop);

        var targetKey = targetDesktop.Id.ToString("B");
        var previousAssignment = Config.Assignments.TryGetValue(targetKey, out var previous)
            ? previous
            : string.Empty;

        Config.Assignments[targetKey] = originalDesktop;
        Config.InitialDesktopImportPromptCompleted = true;
        _configService.Save(Config);

        var targetRealmName = GetAssignmentDisplayName(originalDesktop);

        EnsureIconLayoutsNotDisabledForSession();
        if (saveLayout && Config.IconLayoutPersistenceEnabled)
        {
            _iconLayouts.Save(targetDesktop.Id, targetRealmName, Config.IconLayoutWorkerTimeoutMs);
            _logger.Info($"Initial Desktop import layout saved for linked original Desktop: {targetRealmName} {targetDesktop.Id:B}.");
        }

        _lastDesktopId = null;
        _lastMessage = $"Original Desktop associated with {targetDesktop.Name} without moving files.";
        _logger.Info(
            $"Initial Desktop import completed: target={targetDesktop.Name} {targetDesktop.Id:B}, " +
            $"mode=link-original-desktop, original={originalDesktop}, previousAssignment={previousAssignment}, saveLayout={saveLayout}.");
    }

    public void SaveIconLayoutNow(bool overwriteLockedLayout = false)
    {
        if (_config is null)
        {
            Initialize();
        }

        EnsureIconLayoutPersistenceEnabled();
        var knownDesktop = _knownFolder.GetDesktopPath();
        if (!TryFindAssignmentByRealmPath(knownDesktop, out var desktopId, out var realmName))
        {
            throw new InvalidOperationException(
                "Cannot save icon layout: the active known Desktop does not match any assigned DeskRealm realm. " +
                $"Active Desktop: {knownDesktop}");
        }

        EnsureKnownDesktopAssignmentIsCurrentDesktop(desktopId, realmName, "manual-save");
        EnsureIconLayoutsNotDisabledForSession();
        if (IsLayoutOrRealmLocked(desktopId))
        {
            if (!overwriteLockedLayout)
            {
                throw new InvalidOperationException(
                    "Locked layout: a manual save would overwrite protected positions. " +
                    "Confirm the overwrite explicitly from the DeskRealm UI.");
            }

            _logger.Warn($"Locked icon layout manual overwrite confirmed: {realmName} {desktopId:B}.");
        }

        _iconLayouts.SaveCurrentVariant(desktopId, realmName, Config.IconLayoutWorkerTimeoutMs);
        _lastMessage = $"Current icon layout variant saved: {realmName}";
        _logger.Info(_lastMessage);
    }

    public void RestoreIconLayoutNow()
    {
        if (_config is null)
        {
            Initialize();
        }

        EnsureIconLayoutPersistenceEnabled();
        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        var realmPath = ResolveRealmPath(current, createIfMissing: true);
        var knownDesktop = _knownFolder.GetDesktopPath();

        if (!PathsEqual(knownDesktop, realmPath))
        {
            throw new InvalidOperationException(
                "Cannot restore icon layout: the active known Desktop is not the current virtual desktop realm. " +
                $"Expected: {realmPath}. Current: {knownDesktop}. Run Refresh now first.");
        }

        EnsureIconLayoutsNotDisabledForSession();

        var realmName = Path.GetFileName(realmPath);
        _iconLayouts.RestoreWhenReady(
            current.Id,
            realmName,
            realmPath,
            Config.ShellViewReadyTimeoutMs,
            Config.IconLayoutRestoreVerificationTimeoutMs,
            Config.IconLayoutWorkerTimeoutMs);
        _lastMessage = $"Icon layout restored: {Path.GetFileName(realmPath)}";
        _logger.Info(_lastMessage);
    }

    public void SetEnabled(bool enabled)
    {
        Config.Enabled = enabled;
        _configService.Save(Config);
        _lastMessage = enabled ? "DeskRealm enabled." : "DeskRealm paused.";
        _logger.Info(_lastMessage);
    }

    public void RestoreOriginalDesktop()
    {
        SaveIconLayoutForKnownDesktopIfRealm(_knownFolder.GetDesktopPath(), "restore-original/save-before-restore");

        var original = Config.OriginalDesktopPath
            ?? throw new InvalidOperationException("originalDesktopPath is missing from config.");

        if (!Directory.Exists(original))
        {
            throw new DirectoryNotFoundException($"Original Desktop not found: {original}");
        }

        _knownFolder.SetDesktopPath(original);
        _shellRefresh.RefreshDesktop(original);
        _lastDesktopId = null;
        _lastSwitchAt = DateTimeOffset.Now;
        _lastMessage = $"Original Desktop restored: {original}";
        _logger.Info(_lastMessage);
    }


    private void EnsureDeskRealmEnabledForOperation(string operation)
    {
        if (!Config.Enabled)
        {
            throw new InvalidOperationException($"DeskRealm is disabled. Enable realm switching automation before running: {operation}.");
        }
    }

    private (VirtualDesktopInfo Desktop, string RealmPath, string RealmName) GetActiveCurrentRealmOrThrow(string operation)
    {
        EnsureIconLayoutPersistenceEnabled();
        EnsureIconLayoutsNotDisabledForSession();

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        var realmPath = ResolveRealmPath(current, createIfMissing: true);
        var knownDesktop = _knownFolder.GetDesktopPath();

        if (!PathsEqual(knownDesktop, realmPath))
        {
            throw new InvalidOperationException(
                $"Cannot perform operation '{operation}': the active known Desktop is not the current virtual desktop realm. " +
                $"Expected: {realmPath}. Current: {knownDesktop}. Run Refresh now first.");
        }

        var key = current.Id.ToString("B");
        var realmName = Config.Assignments.TryGetValue(key, out var assignment)
            ? GetAssignmentDisplayName(assignment)
            : Path.GetFileName(realmPath);

        return (current, realmPath, realmName);
    }

    private void SaveCurrentIconLayoutBaseline(VirtualDesktopInfo desktop, string realmName, string reason)
    {
        _iconLayouts.Save(desktop.Id, realmName, Config.IconLayoutWorkerTimeoutMs);
        _logger.Info($"Icon layout baseline saved for lock ({reason}): {realmName} {desktop.Id:B}");
    }


    private static void CreateFolderShortcut(string shortcutPath, string targetPath, string description)
    {
        object? shell = null;
        object? shortcut = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell unavailable: cannot create a Windows .lnk shortcut.");

            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Cannot create WScript.Shell: empty COM instance.");

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: new object[] { shortcutPath })
                ?? throw new InvalidOperationException($"Cannot create shortcut: {shortcutPath}");

            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { @"%SystemRoot%\System32\imageres.dll,3" });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static bool IsPathInsideOrEqual(string candidatePath, string rootPath)
    {
        var candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase);
    }

    public DesktopSwitchStatus GetStatus()
    {
        var known = _knownFolder.GetDesktopPath();
        string name = "—";
        string guid = "—";
        string realm = "—";
        string assignments = "—";

        try
        {
            var desktops = _virtualDesktop.GetVirtualDesktops();
            var current = _virtualDesktop.GetCurrentVirtualDesktop();
            name = $"{current.Name} #{current.Number}";
            guid = current.Id.ToString("B");
            realm = ResolveRealmPath(current, createIfMissing: false);
            assignments = BuildAssignmentsStatus(desktops);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Status virtual desktop read failed: {ex.Message}");
        }

        return new DesktopSwitchStatus(
            name,
            guid,
            realm,
            known,
            _lastSwitchAt,
            _lastMessage,
            assignments);
    }

    private void EnsureInitialized()
    {
        if (_config is null)
        {
            Initialize();
        }
    }

    private static string ToConfigGuidKey(Guid desktopId)
    {
        if (desktopId == Guid.Empty) throw new InvalidOperationException("A Windows virtual-desktop GUID cannot be empty.");
        return desktopId.ToString("D");
    }

    private void MigrateConfigV12RealmMetadata()
    {
        if (Config.Version >= 12)
        {
            return;
        }

        var desktops = _virtualDesktop.GetVirtualDesktops().OrderBy(desktop => desktop.Number).ToList();
        if (desktops.Count == 0)
        {
            throw new InvalidOperationException("Config v12 migration cannot attach legacy hotkeys because Windows returned no virtual desktops.");
        }

        var migrated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var legacy in (Config.LegacyDesktopHotkeys ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)).OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!int.TryParse(legacy.Key, out var number))
            {
                _logger.Warn($"Config v12 migration ignored non-numeric legacy desktopHotkeys key: {legacy.Key}.");
                continue;
            }

            var target = desktops.FirstOrDefault(desktop => desktop.Number == number);
            if (target is null)
            {
                _logger.Warn($"Config v12 migration could not map legacy hotkey #{number}={legacy.Value}: the Windows desktop does not currently exist.");
                continue;
            }

            var normalized = HotkeyParser.Parse(target.Id, legacy.Value).Text;
            if (migrated.Values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Config v12 migration found duplicate hotkey '{normalized}'. Resolve the legacy desktopHotkeys manually before DeskRealm can continue.");
            }

            migrated.Add(ToConfigGuidKey(target.Id), normalized);
        }

        Config.RealmHotkeys = migrated;
        Config.LegacyDesktopHotkeys = null;
        Config.RealmProfiles ??= new Dictionary<string, RealmProfile>(StringComparer.OrdinalIgnoreCase);
        Config.Version = 12;
        _configService.Save(Config);
        _logger.Info($"Config migration v12: {migrated.Count} legacy desktop-number hotkey(s) migrated to Windows virtual-desktop GUID keys. The legacy desktopHotkeys payload was removed after successful binding.");
    }

    private void MigrateConfigV13RealmStudioMetadata()
    {
        Config.RealmWallpapers ??= new Dictionary<string, RealmWallpaper>(StringComparer.OrdinalIgnoreCase);
        Config.ArchivedRealmProfiles ??= new Dictionary<string, ArchivedRealmProfile>(StringComparer.OrdinalIgnoreCase);
        if (Config.Version >= 13)
        {
            return;
        }

        // v0.7.0 makes the favorite/default realm the only startup target. Old explicit
        // startup assignments are preserved by promoting one to the default star.
        var startup = Config.RealmProfiles.FirstOrDefault(pair => pair.Value.ActivateOnDeskRealmStartup);
        if (!string.IsNullOrWhiteSpace(startup.Key))
        {
            foreach (var profile in Config.RealmProfiles.Values)
            {
                profile.IsFavorite = false;
                profile.ActivateOnDeskRealmStartup = false;
            }
            startup.Value.IsFavorite = true;
            startup.Value.ActivateOnDeskRealmStartup = true;
        }

        Config.Version = 13;
        _configService.Save(Config);
        _logger.Info("Config migration v13: Realm Studio wallpaper metadata, archived-realm reuse records and default-realm startup semantics added. GUID-bound wallpaper/default/hotkey keys remain stable across rename; realm-path lock keys migrate on rename.");
    }

    private void MigrateConfigV14AdaptiveRecoveryDefaults()
    {
        if (Config.Version >= 14)
        {
            return;
        }

        var shellDefaultRaised = false;
        var stepDefaultRaised = false;
        if (Config.ShellViewReadyTimeoutMs == 2500)
        {
            Config.ShellViewReadyTimeoutMs = 5000;
            shellDefaultRaised = true;
        }

        if (Config.DesktopStepConfirmationTimeoutMs == 1800)
        {
            Config.DesktopStepConfirmationTimeoutMs = 3000;
            stepDefaultRaised = true;
        }

        Config.Version = 14;
        _configService.Save(Config);
        _logger.Info(
            "Config migration v14: adaptive Explorer recovery defaults applied. " +
            $"shellViewReadyTimeoutMs={(shellDefaultRaised ? "2500 -> 5000" : "custom preserved")}; " +
            $"desktopStepConfirmationTimeoutMs={(stepDefaultRaised ? "1800 -> 3000" : "custom preserved")}. " +
            "A bounded Shell readiness timeout now schedules explicit recovery instead of disabling icon persistence for the entire session.");
    }

    private void MigrateConfigV15RealmRenameApplyMode()
    {
        if (Config.Version >= 15)
        {
            return;
        }

        if (!Enum.IsDefined(Config.RealmRenameApplyMode))
        {
            Config.RealmRenameApplyMode = RealmRenameApplyMode.Ask;
        }

        Config.Version = 15;
        _configService.Save(Config);
        _logger.Info("Config migration v15: realm-name application policy initialized to Ask. Explorer restart remains explicit.");
    }

    private void MigrateConfigV16CanonicalRealmModel()
    {
        if (Config.Version >= 16)
        {
            return;
        }

        Config.Version = 16;
        _configService.Save(Config);
        _logger.Info("Config migration v16: retired legacy realm-name sync and wallpaper switches. The canonical GUID-bound realm model is now active.");
    }

    private void MigrateConfigV17DirectControlDefaults()
    {
        if (Config.Version >= 17)
        {
            return;
        }

        Config.Version = 17;
        _configService.Save(Config);
        _logger.Info("Config migration v17: direct Realm Studio controls use one deterministic default-realm invariant and Windows-to-DeskRealm wallpaper reconciliation.");
    }

    private void MigrateConfigV18StartupVisibility()
    {
        if (Config.Version >= 18)
        {
            return;
        }

        // Existing versions always hid Realm Studio after normal startup. Preserve that
        // established behavior during upgrade; new configurations carry the same default.
        Config.StartMinimized = true;
        Config.Version = 18;
        _configService.Save(Config);
        _logger.Info("Config migration v18: startup visibility preference initialized to Start minimized (preserving the prior notification-area launch behavior).");
    }

    private void EnsureSingleDefaultRealm(IReadOnlyList<VirtualDesktopInfo> desktops)
    {
        if (desktops.Count == 0)
        {
            throw new InvalidOperationException("DeskRealm cannot establish a default realm because Windows returned no virtual desktops.");
        }

        var ordered = desktops.OrderBy(desktop => desktop.Number).ToList();
        var configuredDefaults = ordered
            .Where(desktop => Config.RealmProfiles.TryGetValue(ToConfigGuidKey(desktop.Id), out var profile) && profile.IsFavorite)
            .ToList();

        VirtualDesktopInfo selected;
        if (configuredDefaults.Count > 0)
        {
            selected = configuredDefaults[0];
        }
        else
        {
            selected = ordered.FirstOrDefault(desktop => IsNativeDesktopRealm(desktop.Id)) ?? ordered[0];
        }

        var changed = configuredDefaults.Count != 1 || configuredDefaults[0].Id != selected.Id;
        foreach (var desktop in ordered)
        {
            var key = ToConfigGuidKey(desktop.Id);
            Config.RealmProfiles.TryGetValue(key, out var existing);
            var shouldBeDefault = desktop.Id == selected.Id;
            if (existing is null)
            {
                Config.RealmProfiles[key] = new RealmProfile
                {
                    IsFavorite = shouldBeDefault,
                    ActivateOnDeskRealmStartup = shouldBeDefault
                };
                changed = true;
                continue;
            }

            if (existing.IsFavorite != shouldBeDefault || existing.ActivateOnDeskRealmStartup != shouldBeDefault)
            {
                existing.IsFavorite = shouldBeDefault;
                existing.ActivateOnDeskRealmStartup = shouldBeDefault;
                changed = true;
            }
        }

        if (!changed) return;
        _configService.Save(Config);
        _logger.Info($"Default realm invariant reconciled: {selected.Name} {selected.Id:B}. Native Desktop is preferred only when no default was previously selected.");
    }

    private bool ApplyRealmTargetWithoutSourceSave(
        VirtualDesktopInfo desktop,
        string realmPath,
        string reason)
    {
        var operation = Stopwatch.StartNew();
        var currentKnownDesktop = _knownFolder.GetDesktopPath();

        if (!string.Equals(currentKnownDesktop, realmPath, StringComparison.OrdinalIgnoreCase))
        {
            var knownFolderWatch = Stopwatch.StartNew();
            _knownFolder.SetDesktopPath(realmPath);
            knownFolderWatch.Stop();
            _logger.Info(
                $"[PERF] known-folder switch: reason={reason}, desktop={desktop.Name}, " +
                $"elapsed={knownFolderWatch.Elapsed.TotalMilliseconds:0.0} ms.");
        }
        else
        {
            _logger.Info(
                $"Realm target preparation: Known Folder already points to {realmPath} ({reason}).");
        }

        _shellRefresh.RefreshDesktop(realmPath);
        var iconLayoutReady = RestoreIconLayoutForDesktop(desktop, realmPath, reason);
        operation.Stop();
        _logger.Info(
            $"[PERF] realm target preparation complete: reason={reason}, desktop={desktop.Name}, " +
            $"iconLayoutReady={iconLayoutReady}, elapsed={operation.Elapsed.TotalMilliseconds:0.0} ms.");
        return iconLayoutReady;
    }

    private void CommitRealmState(VirtualDesktopInfo desktop)
    {
        if (_shellReadinessRetryDesktopId.HasValue && _shellReadinessRetryDesktopId.Value != desktop.Id)
        {
            ClearShellReadinessRecovery("Windows committed another realm before the pending Explorer recovery could run.");
        }

        ApplyWallpaperForCommittedRealm(desktop);
        _startupRealmRestorePending = false;
        _lastDesktopId = desktop.Id;
        _lastSwitchAt = DateTimeOffset.Now;
    }

    private void ApplyWallpaperForCommittedRealm(VirtualDesktopInfo desktop)
    {
        if (!Config.RealmWallpapers.TryGetValue(ToConfigGuidKey(desktop.Id), out var wallpaper)) return;
        _wallpapers.ApplyForActiveDesktop(desktop.Id, wallpaper);
    }

    private static Exception? GetTaskFailure(Task task)
    {
        if (task.Exception is null)
        {
            return null;
        }

        return task.Exception.Flatten().InnerExceptions.FirstOrDefault();
    }

    private void SwitchTo(VirtualDesktopInfo desktop, string realmPath, bool force = false)
    {
        var operation = Stopwatch.StartNew();
        var currentKnownDesktop = _knownFolder.GetDesktopPath();

        if (!force && string.Equals(currentKnownDesktop, realmPath, StringComparison.OrdinalIgnoreCase))
        {
            if (_startupRealmRestorePending)
            {
                var startupRestoreReady = RestoreIconLayoutForDesktop(
                    desktop,
                    realmPath,
                    "startup-existing-realm");

                _lastDesktopId = desktop.Id;
                _lastSwitchAt = DateTimeOffset.Now;
                _startupRealmRestorePending = false;
                operation.Stop();
                _lastMessage = startupRestoreReady
                    ? $"Startup layout reconciled: {desktop.Name} -> {realmPath}"
                    : IsShellReadinessRecoveryPendingFor(desktop.Id)
                        ? $"Startup realm detected: Explorer layout recovery is pending for {desktop.Name}."
                        : $"Startup realm detected, but icon persistence was disabled: {_iconLayoutsDisabledReason}";
                _logger.Info(_lastMessage);
                _logger.Info(
                    $"[PERF] startup existing-realm reconciliation complete: desktop={desktop.Name}, " +
                    $"elapsed={operation.Elapsed.TotalMilliseconds:0.0} ms.");

                if (!startupRestoreReady && !IsShellReadinessRecoveryPendingFor(desktop.Id))
                {
                    throw new InvalidOperationException(
                        "The startup realm was detected, but its icon layout could not be restored. " +
                        "Icon layout persistence is disabled for this session. Reason: " + _iconLayoutsDisabledReason);
                }

                if (!startupRestoreReady)
                {
                    _lastMessage = $"Startup realm detected: Explorer layout recovery is pending for {desktop.Name}.";
                    _logger.Warn(_lastMessage);
                }

                return;
            }

            _lastDesktopId = desktop.Id;
            _lastMessage = $"Already on {desktop.Name} -> {Path.GetFileName(realmPath)}";
            return;
        }

        SaveIconLayoutForKnownDesktopIfRealm(currentKnownDesktop);
        var iconLayoutReady = ApplyRealmTargetWithoutSourceSave(desktop, realmPath, "switch");
        CommitRealmState(desktop);
        operation.Stop();
        _lastMessage = iconLayoutReady
            ? $"{desktop.Name} -> {realmPath}"
            : IsShellReadinessRecoveryPendingFor(desktop.Id)
                ? $"{desktop.Name} -> {realmPath}; Explorer layout recovery is pending."
                : $"{desktop.Name} -> {realmPath}; icon persistence disabled for this session: {_iconLayoutsDisabledReason}";
        _logger.Info(_lastMessage);
        _logger.Info(
            $"[PERF] realm reconciliation complete: desktop={desktop.Name}, elapsed={operation.Elapsed.TotalMilliseconds:0.0} ms.");

        if (!iconLayoutReady && !IsShellReadinessRecoveryPendingFor(desktop.Id))
        {
            throw new InvalidOperationException(
                "The realm switch completed, but icon layout persistence failed and was disabled for this session. " +
                "Restart DeskRealm after reviewing the log. Reason: " + _iconLayoutsDisabledReason);
        }

        if (!iconLayoutReady)
        {
            _lastMessage = $"{desktop.Name} -> {realmPath}; Explorer layout recovery is pending.";
            _logger.Warn(_lastMessage);
        }
    }

    private string ResolveRealmPath(VirtualDesktopInfo desktop, bool createIfMissing)
    {
        var key = desktop.Id.ToString("B");
        if (!Config.Assignments.TryGetValue(key, out var realmName) || string.IsNullOrWhiteSpace(realmName))
        {
            if (!createIfMissing)
            {
                return "—";
            }

            realmName = GetDesiredRealmFolderName(desktop);

            EnsureRealmNameNotAssignedToAnotherDesktop(key, realmName);
            Config.Assignments[key] = realmName;
            _configService.Save(Config);
            _logger.Info($"Realm assignment created: {desktop.Name} {key} -> {realmName}");
        }

        if (IsAbsoluteRealmAssignment(realmName))
        {
            if (!Directory.Exists(realmName))
            {
                throw new DirectoryNotFoundException($"External realm missing : {realmName}");
            }

            return realmName;
        }

        realmName = SyncAssignedRealmNameWithDesktopName(desktop, realmName, createIfMissing);

        var realmPath = Path.Combine(Config.RealmsRoot!, realmName);
        if (createIfMissing)
        {
            Directory.CreateDirectory(realmPath);
        }

        if (!Directory.Exists(realmPath))
        {
            throw new DirectoryNotFoundException($"Realm missing : {realmPath}");
        }

        return realmPath;
    }

    private string SyncAssignedRealmNameWithDesktopName(VirtualDesktopInfo desktop, string currentRealmName, bool createIfMissing)
    {
        var key = desktop.Id.ToString("B");
        var desiredRealmName = GetDesiredRealmFolderName(desktop);

        // Explorer can temporarily omit a desktop Name while rebuilding after a deliberate
        // restart. Never rename a known realm folder from its established assignment to a
        // provisional `Desktop N` label; wait for a Registry-confirmed name instead.
        if (desktop.NameIsFallback)
        {
            if (_provisionalDesktopNameSyncWarnings.Add(desktop.Id))
            {
                _logger.Warn(
                    $"Virtual desktop {desktop.Id:B} currently has provisional name '{desktop.Name}'. " +
                    $"Retaining managed realm assignment '{currentRealmName}' until Explorer republishes confirmed metadata.");
            }

            return currentRealmName;
        }

        _provisionalDesktopNameSyncWarnings.Remove(desktop.Id);
        if (string.Equals(currentRealmName, desiredRealmName, StringComparison.OrdinalIgnoreCase))
        {
            return currentRealmName;
        }

        var liveDesktopIds = _virtualDesktop.GetVirtualDesktops().Select(candidate => candidate.Id).ToHashSet();
        var duplicateActiveAssignment = Config.Assignments.FirstOrDefault(pair =>
            !string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) &&
            IsAssignmentOwnedByLiveDesktop(pair.Key, liveDesktopIds) &&
            !IsAbsoluteRealmAssignment(pair.Value) &&
            string.Equals(pair.Value, desiredRealmName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(duplicateActiveAssignment.Key))
        {
            _logger.Warn($"Windows desktop name '{desktop.Name}' collides with an active realm folder. DeskRealm retains '{currentRealmName}' until you resolve the duplicate explicitly in Realm Studio.");
            return currentRealmName;
        }

        var currentPath = Path.Combine(Config.RealmsRoot!, currentRealmName);
        var desiredPath = Path.Combine(Config.RealmsRoot!, desiredRealmName);

        if (PathsEqual(currentPath, desiredPath))
        {
            Config.Assignments[key] = desiredRealmName;
            _configService.Save(Config);
            return desiredRealmName;
        }

        var currentExists = Directory.Exists(currentPath);
        var desiredExists = Directory.Exists(desiredPath);

        if (currentExists && desiredExists)
        {
            throw new InvalidOperationException(
                "Realm rename conflict. " +
                $"DeskRealm wanted to rename '{currentPath}' to '{desiredPath}', but the target folder already exists. " +
                "Rename or merge one of the folders manually, then run Sync names now again.");
        }

        if (currentExists)
        {
            TemporarilyRestoreDesktopIfActiveRealm(currentPath, desiredRealmName);
            Directory.Move(currentPath, desiredPath);
            _logger.Info($"Realm folder renamed: {currentPath} -> {desiredPath}");
        }
        else if (desiredExists)
        {
            _logger.Info($"Realm folder adopted after external rename: {desiredPath}");
        }
        else if (createIfMissing)
        {
            Directory.CreateDirectory(desiredPath);
            _logger.Info($"Realm folder created from virtual desktop name: {desiredPath}");
        }
        else
        {
            return currentRealmName;
        }

        Config.Assignments[key] = desiredRealmName;
        _configService.Save(Config);
        _lastDesktopId = null;
        _lastMessage = $"Name synchronized: {desktop.Name} -> {desiredRealmName}";
        return desiredRealmName;
    }

    private void SyncAllRealmFolderNames(bool createIfMissing, bool reswitchCurrentDesktop)
    {
        var desktops = _virtualDesktop.GetVirtualDesktops();
        foreach (var desktop in desktops)
        {
            ResolveRealmPath(desktop, createIfMissing);
        }

        if (reswitchCurrentDesktop)
        {
            var current = _virtualDesktop.GetCurrentVirtualDesktop();
            var realmPath = ResolveRealmPath(current, createIfMissing: true);
            SwitchTo(current, realmPath, force: true);
        }

        _lastMessage = "Realm names synchronized with Win+Tab.";
        _logger.Info(_lastMessage);
    }

    private void TemporarilyRestoreDesktopIfActiveRealm(string activeRealmPathCandidate, string desiredRealmName)
    {
        var knownDesktopPath = _knownFolder.GetDesktopPath();
        if (!PathsEqual(knownDesktopPath, activeRealmPathCandidate))
        {
            return;
        }

        var original = Config.OriginalDesktopPath
            ?? throw new InvalidOperationException("originalDesktopPath is missing from config.");

        if (!Directory.Exists(original))
        {
            throw new DirectoryNotFoundException($"Original Desktop not found: {original}");
        }

        _logger.Info($"Active realm folder rename detected. Temporary Desktop restore before folder rename -> {desiredRealmName}");
        SaveIconLayoutForKnownDesktopIfRealm(knownDesktopPath);
        _knownFolder.SetDesktopPath(original);
        _shellRefresh.RefreshDesktop(original);
    }

    private void TrackDisplayTopology(string reason)
    {
        if (!Config.IconLayoutDisplayTopologyGuardEnabled || !Config.IconLayoutPersistenceEnabled)
        {
            return;
        }

        var topology = DisplayTopologyService.Capture();
        if (string.IsNullOrWhiteSpace(_lastDisplayTopologyKey))
        {
            _lastDisplayTopologyKey = topology.Key;
            _logger.Info($"Display topology baseline established ({reason}): {topology.Key}.");
            return;
        }

        if (string.Equals(_lastDisplayTopologyKey, topology.Key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastDisplayTopologyKey = topology.Key;
        _displayTopologyRestorePending = true;
        _lastMessage = "Display topology changed: icon layout restore pending.";
        _logger.Warn(
            $"Display topology changed ({reason}): {topology.Key} " +
            $"({topology.Screens.Count} screen(s), virtual={topology.VirtualBoundsWidth}x{topology.VirtualBoundsHeight}). " +
            "Icon layout saves are guarded until the current realm is restored through adaptive Shell readiness.");
    }

    private bool IsDisplayTopologySaveGuardActive(string reason)
    {
        if (!Config.IconLayoutDisplayTopologyGuardEnabled || !_displayTopologyRestorePending)
        {
            return false;
        }

        _logger.Info(
            $"Icon layout {reason} skipped: display topology restore is pending. " +
            "Restore first to avoid saving Windows-compacted icon positions.");
        return true;
    }

    private void RestoreIconLayoutAfterDisplayTopologyChange(VirtualDesktopInfo current, string realmPath)
    {
        if (!Config.IconLayoutDisplayTopologyGuardEnabled || !_displayTopologyRestorePending)
        {
            return;
        }

        _logger.Info("Display topology change confirmed. Restoring current realm layout through adaptive Shell readiness.");
        if (RestoreIconLayoutForDesktop(current, realmPath, "display-topology-change"))
        {
            _displayTopologyRestorePending = false;
            _lastMessage = "Display topology changed: icon layout restored.";
        }
        else if (IsShellReadinessRecoveryPendingFor(current.Id))
        {
            _lastMessage = "Display topology changed: Explorer is still settling; layout recovery retry is pending.";
            _logger.Warn(_lastMessage);
        }
        else
        {
            _displayTopologyRestorePending = false;
            _lastMessage = "Display topology changed: adaptive icon restore failed; icon persistence is disabled until restart.";
            throw new InvalidOperationException(
                "Display topology reconciliation failed. Icon layout persistence is disabled for this session; " +
                "restart DeskRealm after reviewing the log. Reason: " + _iconLayoutsDisabledReason);
        }
    }

    private void SaveIconLayoutForKnownDesktopIfRealm(string knownDesktopPath, string reason = "switch-save")
    {
        if (!Config.IconLayoutPersistenceEnabled)
        {
            return;
        }

        if (_iconLayoutsDisabledForSession)
        {
            _logger.Warn($"Icon layout {reason} skipped: feature disabled for this session. Reason: {_iconLayoutsDisabledReason}");
            return;
        }

        TrackDisplayTopology($"{reason}/save-guard");
        if (IsDisplayTopologySaveGuardActive(reason))
        {
            return;
        }

        if (!TryFindAssignmentByRealmPath(knownDesktopPath, out var desktopId, out var realmName))
        {
            _logger.Info($"Icon layout {reason} skipped: active Desktop is not an assigned DeskRealm realm ({knownDesktopPath}).");
            return;
        }

        try
        {
            if (!IsKnownDesktopAssignmentCurrentDesktop(desktopId, realmName, reason))
            {
                return;
            }

            if (IsLayoutOrRealmLocked(desktopId))
            {
                _iconLayouts.SaveLockedMergeNewIcons(desktopId, realmName, Config.IconLayoutWorkerTimeoutMs);
                _logger.Info($"Icon layout {reason} locked/merge checked: {realmName} {desktopId:B}");
                return;
            }

            _iconLayouts.SaveIfChanged(desktopId, realmName, Config.IconLayoutWorkerTimeoutMs);
            _logger.Info($"Icon layout {reason} checked/saved: {realmName} {desktopId:B}");
        }
        catch (Exception ex)
        {
            DisableIconLayoutsForSession(reason, ex);
        }
    }

    private bool IsKnownDesktopAssignmentCurrentDesktop(Guid assignedDesktopId, string realmName, string reason)
    {
        var currentDesktop = _virtualDesktop.GetCurrentVirtualDesktop();
        if (currentDesktop.Id == assignedDesktopId)
        {
            return true;
        }

        _logger.Info(
            $"Icon layout {reason} skipped: active known Desktop belongs to realm '{realmName}' {assignedDesktopId:B}, " +
            $"but current Windows virtual desktop is '{currentDesktop.Name}' {currentDesktop.Id:B}. " +
            "Skipping save to prevent cross-desktop icon position contamination.");
        return false;
    }

    private void EnsureKnownDesktopAssignmentIsCurrentDesktop(Guid assignedDesktopId, string realmName, string reason)
    {
        if (IsKnownDesktopAssignmentCurrentDesktop(assignedDesktopId, realmName, reason))
        {
            return;
        }

        var currentDesktop = _virtualDesktop.GetCurrentVirtualDesktop();
        throw new InvalidOperationException(
            "Icon layout save refused: the active known Desktop belongs to realm " +
            $"'{realmName}' {assignedDesktopId:B}, but the current Windows virtual desktop is " +
            $"'{currentDesktop.Name}' {currentDesktop.Id:B}. " +
            "Wait until DeskRealm has completed the switch, then use Save icon layout now from the active realm.");
    }

    private bool RestoreIconLayoutForDesktop(VirtualDesktopInfo desktop, string realmPath, string reason = "auto-restore")
    {
        if (!Config.IconLayoutPersistenceEnabled)
        {
            return true;
        }

        if (_iconLayoutsDisabledForSession)
        {
            _logger.Warn($"Icon layout {reason} skipped: feature disabled for this session. Reason: {_iconLayoutsDisabledReason}");
            return false;
        }

        var realmName = Path.GetFileName(realmPath);
        try
        {
            _iconLayouts.RestoreWhenReady(
                desktop.Id,
                realmName,
                realmPath,
                Config.ShellViewReadyTimeoutMs,
                Config.IconLayoutRestoreVerificationTimeoutMs,
                Config.IconLayoutWorkerTimeoutMs);
            _logger.Info($"Icon layout {reason} restored adaptively: {realmName} {desktop.Id:B}.");
            _displayTopologyRestorePending = false;
            if (IsShellReadinessRecoveryPendingFor(desktop.Id))
            {
                ClearShellReadinessRecovery("Explorer stabilized during a later target-preparation pass.");
            }
            return true;
        }
        catch (IconLayoutShellReadinessTimeoutException ex)
        {
            ScheduleShellReadinessRecovery(desktop, realmPath, reason, ex);
            return false;
        }
        catch (Exception ex)
        {
            DisableIconLayoutsForSession(reason, ex);
            return false;
        }
    }

    private bool IsShellReadinessRecoveryPendingFor(Guid desktopId)
    {
        return _shellReadinessRetryDesktopId.HasValue && _shellReadinessRetryDesktopId.Value == desktopId;
    }

    private bool TryRunScheduledShellReadinessRecovery(VirtualDesktopInfo current, string realmPath)
    {
        if (!_shellReadinessRetryDesktopId.HasValue)
        {
            return false;
        }

        if (_shellReadinessRetryDesktopId.Value != current.Id ||
            !PathsEqual(_shellReadinessRetryRealmPath ?? string.Empty, realmPath))
        {
            ClearShellReadinessRecovery("The active realm changed before the pending Explorer recovery ran.");
            return false;
        }

        if (DateTimeOffset.Now < _shellReadinessRetryNotBefore)
        {
            return false;
        }

        _logger.Warn(
            $"Running scheduled Explorer readiness recovery {_shellReadinessRetryAttempt}/{MaximumShellReadinessRecoveryAttempts} " +
            $"for {current.Name} {current.Id:B}.");
        if (RestoreIconLayoutForDesktop(current, realmPath, $"shell-readiness-retry-{_shellReadinessRetryAttempt}"))
        {
            ClearShellReadinessRecovery("Explorer stabilized and the icon layout was restored.");
            _lastMessage = $"Explorer layout recovery completed: {current.Name}.";
            _logger.Info(_lastMessage);
        }

        return true;
    }

    private void ScheduleShellReadinessRecovery(
        VirtualDesktopInfo desktop,
        string realmPath,
        string operation,
        IconLayoutShellReadinessTimeoutException exception)
    {
        var sameRealm = IsShellReadinessRecoveryPendingFor(desktop.Id) &&
                        PathsEqual(_shellReadinessRetryRealmPath ?? string.Empty, realmPath);
        var nextAttempt = sameRealm ? _shellReadinessRetryAttempt + 1 : 1;

        if (nextAttempt > MaximumShellReadinessRecoveryAttempts)
        {
            _shellReadinessRetryDesktopId = desktop.Id;
            _shellReadinessRetryRealmPath = realmPath;
            _shellReadinessRetryAttempt = MaximumShellReadinessRecoveryAttempts;
            _shellReadinessRetryNotBefore = DateTimeOffset.MaxValue;
            _lastMessage =
                $"Explorer layout recovery exhausted its bounded retry budget for {desktop.Name}. " +
                "The realm remains active; use Refresh after Explorer settles. No layout persistence was disabled.";
            _logger.Warn(_lastMessage + " Last readiness diagnostic: " + exception.Message);
            return;
        }

        var delay = TimeSpan.FromMilliseconds(nextAttempt switch
        {
            1 => 750,
            2 => 1500,
            _ => 2500
        });

        _shellReadinessRetryDesktopId = desktop.Id;
        _shellReadinessRetryRealmPath = realmPath;
        _shellReadinessRetryAttempt = nextAttempt;
        _shellReadinessRetryNotBefore = DateTimeOffset.Now.Add(delay);
        _lastMessage =
            $"Explorer is still settling {desktop.Name}; icon layout recovery retry {nextAttempt}/{MaximumShellReadinessRecoveryAttempts} " +
            $"is scheduled in {delay.TotalMilliseconds:0} ms.";
        _logger.Warn(
            $"Icon layout {operation} reached its bounded Explorer readiness timeout. " +
            $"{_lastMessage} Diagnostic: {exception.Message}");
        IconLayoutRecoveryScheduled?.Invoke(delay);
    }

    private void ClearShellReadinessRecovery(string reason)
    {
        if (!_shellReadinessRetryDesktopId.HasValue)
        {
            return;
        }

        _logger.Info(
            $"Explorer readiness recovery cleared for {_shellReadinessRetryDesktopId.Value:B}. Reason: {reason}");
        _shellReadinessRetryDesktopId = null;
        _shellReadinessRetryRealmPath = null;
        _shellReadinessRetryNotBefore = DateTimeOffset.MinValue;
        _shellReadinessRetryAttempt = 0;
    }

    private void DisableIconLayoutsForSession(string operation, Exception ex)
    {
        _iconLayoutsDisabledForSession = true;
        _iconLayoutsDisabledReason = $"{operation}: {ex.Message}";
        _lastMessage = "Icon persistence disabled for this session: " + _iconLayoutsDisabledReason;
        _logger.Error("Icon layout persistence failed. DeskRealm disables icon layout persistence for the current session, but keeps desktop switching alive.", ex);
    }

    private bool TryFindAssignmentByRealmPath(string path, out Guid desktopId, out string realmName)
    {
        foreach (var assignment in Config.Assignments)
        {
            var candidatePath = ResolveAssignmentToPath(assignment.Value);
            if (!PathsEqual(path, candidatePath))
            {
                continue;
            }

            if (!Guid.TryParse(assignment.Key, out desktopId))
            {
                throw new InvalidOperationException($"Invalid assignment GUID in config: {assignment.Key}");
            }

            realmName = GetAssignmentDisplayName(assignment.Value);
            return true;
        }

        desktopId = Guid.Empty;
        realmName = string.Empty;
        return false;
    }

    private string ResolveAssignmentToPath(string assignment)
    {
        return IsAbsoluteRealmAssignment(assignment)
            ? assignment
            : Path.Combine(Config.RealmsRoot!, assignment);
    }

    private static bool IsAbsoluteRealmAssignment(string assignment)
    {
        return !string.IsNullOrWhiteSpace(assignment) && Path.IsPathFullyQualified(assignment);
    }

    private bool IsOriginalNativeDesktopAssignment(string assignment)
    {
        var original = Config.OriginalDesktopPath;
        return IsAbsoluteRealmAssignment(assignment) &&
               !string.IsNullOrWhiteSpace(original) &&
               PathsEqual(assignment, original);
    }

    private string GetAssignmentDisplayName(string assignment)
    {
        if (!IsAbsoluteRealmAssignment(assignment))
        {
            return assignment;
        }

        var original = Config.OriginalDesktopPath;
        if (!string.IsNullOrWhiteSpace(original) && PathsEqual(assignment, original))
        {
            return "OriginalDesktop";
        }

        return Path.GetFileName(assignment.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private void EnsureOriginalDesktopNotAssignedToAnotherDesktop(Guid currentDesktopId, string originalDesktop)
    {
        var currentKey = currentDesktopId.ToString("B");
        foreach (var assignment in Config.Assignments)
        {
            if (string.Equals(assignment.Key, currentKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsAbsoluteRealmAssignment(assignment.Value) && PathsEqual(assignment.Value, originalDesktop))
            {
                throw new InvalidOperationException(
                    "Initial Desktop import refused: the original Desktop is already associated with another virtual desktop. " +
                    $"Existing assignment: {assignment.Key} -> {assignment.Value}.");
            }
        }
    }

    private bool IsLayoutLocked(Guid desktopId)
    {
        return Config.LockedIconLayouts.TryGetValue(desktopId.ToString("B"), out var locked) && locked;
    }

    private bool IsRealmLocked(Guid desktopId)
    {
        if (Config.LockedRealms.TryGetValue(desktopId.ToString("B"), out var legacyLocked) && legacyLocked)
        {
            return true;
        }

        if (!TryBuildRealmLockKey(desktopId, out var realmKey))
        {
            return false;
        }

        return Config.LockedRealms.TryGetValue(realmKey, out var locked) && locked;
    }

    private bool IsLayoutOrRealmLocked(Guid desktopId)
    {
        return IsLayoutLocked(desktopId) || IsCurrentVariantLocked(desktopId) || IsRealmLocked(desktopId);
    }

    private bool IsCurrentVariantLocked(Guid desktopId)
    {
        var topology = DisplayTopologyService.Capture();
        return IsVariantLocked(BuildVariantLockKey(desktopId, topology.Key));
    }

    private bool IsVariantLocked(string variantKey)
    {
        return Config.LockedIconLayoutVariants.TryGetValue(variantKey, out var locked) && locked;
    }

    private static string BuildVariantLockKey(Guid desktopId, string displayTopologyKey)
    {
        if (string.IsNullOrWhiteSpace(displayTopologyKey))
        {
            throw new InvalidOperationException("Cannot build icon layout variant lock key: display topology key is empty.");
        }

        return desktopId.ToString("B") + "|" + displayTopologyKey.Trim();
    }

    private VirtualDesktopInfo FindVirtualDesktopOrThrow(Guid desktopId)
    {
        return _virtualDesktop.GetVirtualDesktops().FirstOrDefault(d => d.Id == desktopId)
            ?? throw new InvalidOperationException($"Virtual desktop not found: {desktopId:B}.");
    }

    private bool TryBuildRealmLockKey(Guid desktopId, out string realmKey)
    {
        var key = desktopId.ToString("B");
        if (!Config.Assignments.TryGetValue(key, out var assignment) || string.IsNullOrWhiteSpace(assignment))
        {
            realmKey = string.Empty;
            return false;
        }

        realmKey = BuildRealmLockKey(ResolveAssignmentToPath(assignment));
        return true;
    }

    private static string BuildRealmLockKey(string realmPath)
    {
        return Path.GetFullPath(realmPath.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
    }

    private void RemoveLegacyRealmLockGuidsForRealm(string realmKey)
    {
        var matchingDesktopIds = Config.Assignments
            .Where(a => string.Equals(BuildRealmLockKey(ResolveAssignmentToPath(a.Value)), realmKey, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Key)
            .ToList();

        foreach (var matchingDesktopId in matchingDesktopIds)
        {
            Config.LockedRealms.Remove(matchingDesktopId);
        }
    }

    private void MoveRealmLockPathKey(string oldRealmPath, string newRealmPath)
    {
        var oldKey = BuildRealmLockKey(oldRealmPath);
        if (!Config.LockedRealms.TryGetValue(oldKey, out var locked) || !locked) return;
        Config.LockedRealms.Remove(oldKey);
        Config.LockedRealms[BuildRealmLockKey(newRealmPath)] = true;
        _logger.Info($"Realm lock key migrated after rename: '{oldKey}' -> '{BuildRealmLockKey(newRealmPath)}'.");
    }

    /// <summary>
    /// Retires configuration entries that belong to Windows virtual desktops removed outside
    /// DeskRealm. This runs only during initialization, when DeskRealm reads a fresh stable
    /// Windows desktop snapshot. Only config metadata is changed: managed folders and icon-layout
    /// files remain available through the archived profile. This prevents an old assignment from
    /// blocking a newly enumerated desktop when Explorer temporarily reports a fallback name.
    /// </summary>
    private void RetireStaleRealmMetadata(IReadOnlyList<VirtualDesktopInfo> liveDesktops)
    {
        var liveDesktopIds = liveDesktops.Select(desktop => desktop.Id).ToHashSet();
        var staleDesktopIds = Config.Assignments.Keys
            .Select(key => Guid.TryParse(key, out var desktopId) ? desktopId : Guid.Empty)
            .Where(desktopId => desktopId != Guid.Empty && !liveDesktopIds.Contains(desktopId))
            .Distinct()
            .ToList();

        foreach (var staleDesktopId in staleDesktopIds)
        {
            var assignmentKey = staleDesktopId.ToString("B");
            if (!Config.Assignments.TryGetValue(assignmentKey, out var assignment) || string.IsNullOrWhiteSpace(assignment))
            {
                continue;
            }

            var archivedName = GetAssignmentDisplayName(assignment);
            ArchiveAndRemoveDeletedRealmMetadata(
                new VirtualDesktopInfo(staleDesktopId, archivedName, Number: 0),
                preserveExistingArchive: true);
            _logger.Warn(
                $"Retired stale realm assignment for removed Windows virtual desktop {staleDesktopId:B}: " +
                $"'{archivedName}'. The folder/layout data was not moved and remains available through archived realm reuse.");
        }
    }

    private void ArchiveAndRemoveDeletedRealmMetadata(VirtualDesktopInfo deleted, bool preserveExistingArchive = false)
    {
        var assignmentKey = deleted.Id.ToString("B");
        Config.Assignments.TryGetValue(assignmentKey, out var assignment);
        Config.RealmWallpapers.TryGetValue(ToConfigGuidKey(deleted.Id), out var wallpaper);
        var archiveKey = RealmFolderNameSanitizer.FromVirtualDesktopName(deleted.Name, Config.RealmNameMaxLength);
        if (!preserveExistingArchive || !Config.ArchivedRealmProfiles.ContainsKey(archiveKey))
        {
            Config.ArchivedRealmProfiles[archiveKey] = new ArchivedRealmProfile
            {
                SourceDesktopId = deleted.Id.ToString("D"),
                DesktopName = deleted.Name,
                RealmAssignment = assignment ?? string.Empty,
                Wallpaper = wallpaper,
                ArchivedAt = DateTimeOffset.Now
            };
        }
        else
        {
            _logger.Warn(
                $"Retired desktop {deleted.Id:B} had the archived realm name '{archiveKey}', which already exists. " +
                "The existing archived profile was retained; no folders or layouts were changed.");
        }

        var oldRealmPath = !string.IsNullOrWhiteSpace(assignment) ? ResolveAssignmentToPath(assignment) : string.Empty;
        Config.Assignments.Remove(assignmentKey);
        Config.RealmProfiles.Remove(ToConfigGuidKey(deleted.Id));
        Config.RealmHotkeys.Remove(ToConfigGuidKey(deleted.Id));
        Config.RealmWallpapers.Remove(ToConfigGuidKey(deleted.Id));
        Config.LockedIconLayouts.Remove(deleted.Id.ToString("B"));
        Config.LockedIconLayouts.Remove(deleted.Id.ToString("D"));
        foreach (var key in Config.LockedIconLayoutVariants.Keys.Where(key => key.StartsWith(deleted.Id.ToString("B") + "|", StringComparison.OrdinalIgnoreCase) || key.StartsWith(deleted.Id.ToString("D") + "|", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Config.LockedIconLayoutVariants.Remove(key);
        }

        if (!string.IsNullOrWhiteSpace(oldRealmPath))
        {
            var lockKey = BuildRealmLockKey(oldRealmPath);
            var stillUsed = Config.Assignments.Values.Any(value => string.Equals(BuildRealmLockKey(ResolveAssignmentToPath(value)), lockKey, StringComparison.OrdinalIgnoreCase));
            if (!stillUsed) Config.LockedRealms.Remove(lockKey);
        }

        _configService.Save(Config);
        _logger.Info($"Deleted realm metadata archived under '{archiveKey}'. The realm folder and icon layout source remain retained so reuse is an explicit later choice.");
    }

    private void EnsureIconLayoutPersistenceEnabled()
    {
        if (!Config.IconLayoutPersistenceEnabled)
        {
            throw new InvalidOperationException("Icon layout persistence is disabled in config.");
        }
    }

    private void EnsureIconLayoutsNotDisabledForSession()
    {
        if (_iconLayoutsDisabledForSession)
        {
            throw new InvalidOperationException(
                "Icon layout persistence has been disabled for this session after a worker error. " +
                "Restart DeskRealm after fixing the issue to try again. Last error: " + _iconLayoutsDisabledReason);
        }
    }

    private string GetDesiredRealmFolderName(VirtualDesktopInfo desktop)
    {
        return RealmFolderNameSanitizer.FromVirtualDesktopName(desktop.Name, Config.RealmNameMaxLength);
    }

    private void EnsureRealmNameNotAssignedToAnotherDesktop(string currentKey, string realmName)
    {
        var liveDesktopIds = _virtualDesktop.GetVirtualDesktops().Select(desktop => desktop.Id).ToHashSet();
        var existing = Config.Assignments.FirstOrDefault(pair =>
            !string.Equals(pair.Key, currentKey, StringComparison.OrdinalIgnoreCase) &&
            IsAssignmentOwnedByLiveDesktop(pair.Key, liveDesktopIds) &&
            !IsAbsoluteRealmAssignment(pair.Value) &&
            string.Equals(pair.Value, realmName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(existing.Key))
        {
            throw new InvalidOperationException(
                $"Realm name already assigned: '{realmName}' is already linked to virtual desktop {existing.Key}. " +
                "DeskRealm does not resolve duplicates silently. Rename one of the desktops in Win+Tab.");
        }
    }

    private static bool IsAssignmentOwnedByLiveDesktop(string assignmentKey, IReadOnlySet<Guid> liveDesktopIds)
    {
        return Guid.TryParse(assignmentKey, out var assignmentDesktopId) && liveDesktopIds.Contains(assignmentDesktopId);
    }

    private string BuildAssignmentsStatus(IReadOnlyList<VirtualDesktopInfo> desktops)
    {
        if (desktops.Count == 0)
        {
            return "—";
        }

        return string.Join(Environment.NewLine, desktops.Select(desktop =>
        {
            var key = desktop.Id.ToString("B");
            var rawAssignment = Config.Assignments.TryGetValue(key, out var assignment) ? assignment : "—";
            var realmName = rawAssignment == "—" ? rawAssignment : GetAssignmentDisplayName(rawAssignment);
            var desiredName = GetDesiredRealmFolderName(desktop);
            var assignmentKind = rawAssignment != "—" && IsAbsoluteRealmAssignment(rawAssignment) ? "external-path" : "managed-folder";
            return $"  #{desktop.Number} {desktop.Name} {key} -> {realmName} ({assignmentKind}, desired: {desiredName})";
        }));
    }

    private static bool ContainsOneDriveSegment(string path)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Contains("OneDrive", StringComparison.OrdinalIgnoreCase));
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record IconLayoutRealmSnapshot(
    string RealmPath,
    bool IsLocked,
    IReadOnlyList<IconLayoutEntrySnapshot> Layouts);

internal sealed record IconLayoutEntrySnapshot(
    Guid DesktopId,
    bool IsLayoutLocked,
    bool EffectiveLocked,
    bool HasSavedLayout,
    IReadOnlyList<IconLayoutVariantSnapshot> Variants);

internal sealed record IconLayoutVariantSnapshot(
    string DisplayTopologyKey,
    string Summary,
    DateTimeOffset? SavedAt,
    int IconCount,
    bool IsCurrentTopology,
    bool IsVariantLocked,
    bool EffectiveLocked,
    bool HasSavedLayout,
    bool IsLockedByRealm,
    bool IsLockedByCurrentLayout);

internal sealed class IconLayoutRealmBuilder
{
    public IconLayoutRealmBuilder(string realmPath, int firstDesktopNumber)
    {
        RealmPath = realmPath;
        FirstDesktopNumber = firstDesktopNumber;
    }

    public string RealmPath { get; }
    public int FirstDesktopNumber { get; }
    public List<IconLayoutEntrySnapshot> Layouts { get; } = [];
}
