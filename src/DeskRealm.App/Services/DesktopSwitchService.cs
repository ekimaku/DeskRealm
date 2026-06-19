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

    public DesktopSwitchService(
        RealmConfigService configService,
        KnownFolderService knownFolder,
        VirtualDesktopRegistryService virtualDesktop,
        ShellRefreshService shellRefresh,
        IconLayoutWorkerClientService iconLayouts,
        VirtualDesktopNavigatorService navigator,
        KeyboardInputService keyboard,
        FileLogger logger)
    {
        _configService = configService;
        _knownFolder = knownFolder;
        _virtualDesktop = virtualDesktop;
        _shellRefresh = shellRefresh;
        _iconLayouts = iconLayouts;
        _navigator = navigator;
        _keyboard = keyboard;
        _logger = logger;
    }

    public RealmConfig Config => _config ?? throw new InvalidOperationException("Config not initialized.");

    public string IconLayoutRuntimeStatus => _iconLayoutsDisabledForSession
        ? "DISABLED UNTIL RESTART — " + _iconLayoutsDisabledReason
        : "Active";

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
        if (!Config.SyncRealmNamesWithVirtualDesktopNames)
        {
            EnsureMinimumRealms(4);
        }
        else
        {
            SyncAllRealmFolderNames(createIfMissing: true, reswitchCurrentDesktop: false);
        }

        _lastMessage = "Config loaded.";
        _logger.Info($"Original Desktop: {Config.OriginalDesktopPath}");
        _logger.Info($"Realms root: {Config.RealmsRoot}");
        _logger.Info($"Realm name sync: {Config.SyncRealmNamesWithVirtualDesktopNames}");
        _logger.Info($"Icon layout persistence: {Config.IconLayoutPersistenceEnabled}");
        _logger.Info("Icon layouts are saved on confirmed DeskRealm hotkey transitions, manual save, lock merge and exit restore; legacy periodic polling is retired.");
        _logger.Info($"Icon layout worker timeout: {Config.IconLayoutWorkerTimeoutMs} ms");
        _logger.Info($"Adaptive Shell readiness timeout: {Config.ShellViewReadyTimeoutMs} ms");
        _logger.Info($"Adaptive icon verification timeout: {Config.IconLayoutRestoreVerificationTimeoutMs} ms");
        _logger.Info("Startup layout recovery: the first matching realm is restored once even when the Desktop Known Folder already targets it.");
        _logger.Info($"Desktop hotkeys: {Config.DesktopHotkeysEnabled} / {string.Join(", ", Config.DesktopHotkeys.Select(p => $"#{p.Key}={p.Value}"))}");
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

    public void SwitchToDesktopNumber(int targetNumber)
    {
        if (_config is null)
        {
            Initialize();
        }

        EnsureDeskRealmEnabledForOperation($"hotkey switch to desktop #{targetNumber}");

        var desktops = _virtualDesktop.GetVirtualDesktops();
        var target = desktops.FirstOrDefault(d => d.Number == targetNumber)
            ?? throw new InvalidOperationException($"Virtual desktop #{targetNumber} not found. Available desktops: 1 to {desktops.Count}.");

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        if (current.Id == target.Id)
        {
            var currentRealmPath = ResolveRealmPath(current, createIfMissing: true);
            SwitchTo(current, currentRealmPath, force: false);
            _lastMessage = $"Hotkey desktop #{targetNumber} ignored: already on {current.Name}.";
            _logger.Info(_lastMessage);
            return;
        }

        var operation = Stopwatch.StartNew();
        var targetRealmPath = ResolveRealmPath(target, createIfMissing: true);
        _logger.Info(
            $"[PERF] hotkey preflight complete: target=#{target.Number} {target.Id:B}, realm={targetRealmPath}, " +
            $"elapsed={operation.Elapsed.TotalMilliseconds:0.0} ms.");

        SaveIconLayoutForKnownDesktopIfRealm(_knownFolder.GetDesktopPath(), "hotkey-pre-navigation-save");
        _keyboard.WaitForNavigationModifiersReleased(Config.HotkeyModifierReleaseTimeoutMs);

        _logger.Info(
            $"Hotkey parallel transaction starting: source=#{current.Number} {current.Id:B}, " +
            $"target=#{target.Number} {target.Id:B}, realm={targetRealmPath}.");

        using var startGate = new ManualResetEventSlim(false);
        var navigationTask = Task.Run(() =>
        {
            startGate.Wait();
            return _navigator.NavigateByNumber(
                current,
                target,
                desktops,
                Config.DesktopStepConfirmationTimeoutMs);
        });
        var targetPreparationTask = Task.Run(() =>
        {
            startGate.Wait();
            return ApplyRealmTargetWithoutSourceSave(
                target,
                targetRealmPath,
                "hotkey-parallel-target");
        });

        var parallelWatch = Stopwatch.StartNew();
        startGate.Set();
        try
        {
            Task.WhenAll(navigationTask, targetPreparationTask).GetAwaiter().GetResult();
        }
        catch
        {
            // Both branches are bounded and Task.WhenAll observes both. The final transaction
            // barrier below inspects each result and reconciles the Windows state explicitly.
        }
        parallelWatch.Stop();

        var navigationError = GetTaskFailure(navigationTask);
        var preparationError = GetTaskFailure(targetPreparationTask);
        var preparationReady = targetPreparationTask.Status == TaskStatus.RanToCompletion && targetPreparationTask.Result;
        var actual = _virtualDesktop.GetCurrentVirtualDesktop();

        _logger.Info(
            $"[PERF] hotkey parallel barrier reached: expected=#{target.Number} {target.Id:B}, " +
            $"actual=#{actual.Number} {actual.Id:B}, navigationCompleted={navigationTask.Status == TaskStatus.RanToCompletion}, " +
            $"targetPrepared={preparationReady}, elapsed={parallelWatch.Elapsed.TotalMilliseconds:0.0} ms.");

        if (actual.Id == target.Id)
        {
            if (!preparationReady)
            {
                var speculativeFailure = preparationError?.Message ?? "target preparation returned a degraded result";
                _logger.Warn(
                    $"Hotkey target GUID was confirmed, but parallel realm preparation did not complete cleanly: {speculativeFailure}. " +
                    "Performing one explicit final reconciliation on the confirmed target.");

                preparationReady = ApplyRealmTargetWithoutSourceSave(
                    target,
                    targetRealmPath,
                    "hotkey-final-target-reconcile");
            }

            CommitRealmState(target);
            operation.Stop();

            if (!preparationReady)
            {
                throw new InvalidOperationException(
                    $"Windows reached desktop #{target.Number} {target.Name}, but its target realm layout could not be restored. " +
                    $"Icon layout persistence is disabled for this session. Reason: {_iconLayoutsDisabledReason}");
            }

            if (navigationError is not null)
            {
                _logger.Warn(
                    $"Hotkey navigation reported an intermediate confirmation error, but the final GUID barrier confirmed the requested target: " +
                    navigationError.Message);
            }

            _lastMessage = $"Hotkey -> desktop #{targetNumber} {target.Name}.";
            _logger.Info(_lastMessage);
            _logger.Info(
                $"[PERF] hotkey parallel switch complete: target=#{targetNumber}, " +
                $"elapsed={operation.Elapsed.TotalMilliseconds:0.0} ms.");
            return;
        }

        var actualRealmPath = ResolveRealmPath(actual, createIfMissing: true);
        _logger.Warn(
            $"Hotkey navigation mismatch: expected desktop #{target.Number} {target.Id:B}, " +
            $"but Windows confirmed #{actual.Number} {actual.Id:B}. " +
            $"Discarding prepared target realm and compensating to {actualRealmPath}.");

        var compensationReady = ApplyRealmTargetWithoutSourceSave(
            actual,
            actualRealmPath,
            "hotkey-navigation-mismatch-compensation");
        CommitRealmState(actual);
        operation.Stop();
        _lastMessage = compensationReady
            ? $"Hotkey mismatch compensated to desktop #{actual.Number} {actual.Name}."
            : $"Hotkey mismatch reached desktop #{actual.Number} {actual.Name}; realm selected but icon persistence is disabled.";
        _logger.Warn(_lastMessage);

        var navigationDetail = navigationError is null ? "no navigation exception" : navigationError.Message;
        var preparationDetail = preparationError is null ? "target preparation completed" : preparationError.Message;
        var compensationDetail = compensationReady
            ? "the actual desktop realm was restored and verified"
            : "the actual desktop folder was selected, but icon persistence is disabled for this session";

        throw new InvalidOperationException(
            $"DeskRealm hotkey transaction did not reach desktop #{target.Number} {target.Name}. " +
            $"Windows ended on desktop #{actual.Number} {actual.Name}; {compensationDetail}. " +
            $"Navigation: {navigationDetail}. Target preparation: {preparationDetail}.");
    }

    public void SyncRealmNamesNow()
    {
        if (_config is null)
        {
            Initialize();
        }

        SyncAllRealmFolderNames(createIfMissing: true, reswitchCurrentDesktop: true);
    }

    public bool IsCurrentLayoutLocked()
    {
        if (_config is null)
        {
            Initialize();
        }

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        return IsLayoutLocked(current.Id);
    }

    public bool IsCurrentLayoutVariantLocked()
    {
        if (_config is null)
        {
            Initialize();
        }

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        return IsCurrentVariantLocked(current.Id);
    }

    public bool IsCurrentRealmLocked()
    {
        if (_config is null)
        {
            Initialize();
        }

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        return IsRealmLocked(current.Id);
    }

    public bool IsCurrentLayoutOrRealmLocked()
    {
        if (_config is null)
        {
            Initialize();
        }

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        return IsLayoutOrRealmLocked(current.Id);
    }

    public string GetCurrentLockStatusText()
    {
        if (_config is null)
        {
            Initialize();
        }

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        var realmPath = ResolveRealmPath(current, createIfMissing: false);
        var layoutLocked = IsLayoutLocked(current.Id);
        var variantLocked = IsCurrentVariantLocked(current.Id);
        var realmLocked = IsRealmLocked(current.Id);
        return $"Desktop #{current.Number} {current.Name} / {Path.GetFileName(realmPath)} — variant locked: {variantLocked}, layout locked: {layoutLocked}, realm locked: {realmLocked}";
    }

    public IReadOnlyList<IconLayoutRealmSnapshot> GetIconLayoutLockSnapshot()
    {
        if (_config is null)
        {
            Initialize();
        }

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        var desktops = _virtualDesktop.GetVirtualDesktops().OrderBy(d => d.Number).ToList();
        var groups = new Dictionary<string, IconLayoutRealmBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var desktop in desktops)
        {
            var realmPath = ResolveRealmPath(desktop, createIfMissing: true);
            var realmName = Path.GetFileName(realmPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(realmName))
            {
                realmName = desktop.Name;
            }

            var realmKey = BuildRealmLockKey(realmPath);
            if (!groups.TryGetValue(realmKey, out var builder))
            {
                builder = new IconLayoutRealmBuilder(realmKey, realmName, realmPath, desktop.Number);
                groups.Add(realmKey, builder);
            }

            var layoutLocked = IsLayoutLocked(desktop.Id);
            var realmLocked = IsRealmLocked(desktop.Id);
            var currentTopology = desktop.Id == current.Id ? DisplayTopologyService.Capture().Key : string.Empty;
            var variants = BuildVariantSnapshots(desktop, realmLocked, layoutLocked, currentTopology);
            builder.Layouts.Add(new IconLayoutEntrySnapshot(
                desktop.Id,
                desktop.Number,
                desktop.Name,
                desktop.Id == current.Id,
                layoutLocked,
                realmLocked || layoutLocked,
                variants.Any(v => v.HasSavedLayout),
                variants));
        }

        return groups.Values
            .OrderBy(g => g.RealmNumber)
            .ThenBy(g => g.RealmName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new IconLayoutRealmSnapshot(
                g.RealmKey,
                g.RealmNumber,
                g.RealmName,
                g.RealmPath,
                g.Layouts.Any(l => IsRealmLocked(l.DesktopId)),
                g.Layouts.Any(l => l.IsCurrent),
                g.Layouts.OrderBy(l => l.DesktopNumber).ToList()))
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
                    BuildVariantLockKey(desktop.Id, "pending-baseline"),
                    "pending-baseline",
                    "pending",
                    "No saved icon layout yet",
                    null,
                    0,
                    [],
                    !string.IsNullOrWhiteSpace(currentTopologyKey),
                    false,
                    realmLocked || layoutLocked,
                    false)
            ];
        }

        return fileVariants
            .Select(variant =>
            {
                var variantKey = BuildVariantLockKey(desktop.Id, variant.DisplayTopologyKey);
                var variantLocked = IsVariantLocked(variantKey);
                return new IconLayoutVariantSnapshot(
                    variantKey,
                    variant.DisplayTopologyKey,
                    variant.DisplayTopologyFamilyKey,
                    variant.Summary,
                    variant.SavedAt,
                    variant.IconCount,
                    variant.Displays,
                    !string.IsNullOrWhiteSpace(currentTopologyKey) && string.Equals(variant.DisplayTopologyKey, currentTopologyKey, StringComparison.OrdinalIgnoreCase),
                    variantLocked,
                    realmLocked || layoutLocked || variantLocked,
                    true);
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

    public void LockCurrentIconLayout()
    {
        if (_config is null)
        {
            Initialize();
        }

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        LockIconLayout(current.Id);
    }

    public void UnlockCurrentIconLayout()
    {
        if (_config is null)
        {
            Initialize();
        }

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        UnlockIconLayout(current.Id);
    }

    public void LockCurrentRealmLayouts()
    {
        if (_config is null)
        {
            Initialize();
        }

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        LockRealmLayoutsForDesktop(current.Id);
    }

    public void UnlockCurrentRealmLayouts()
    {
        if (_config is null)
        {
            Initialize();
        }

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        UnlockRealmLayoutsForDesktop(current.Id);
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

    public void MarkInitialDesktopImportSkipped()
    {
        _ = SkipInitialDesktopImportAndCreateOriginalDesktopShortcuts();
    }

    public int SkipInitialDesktopImportAndCreateOriginalDesktopShortcuts()
    {
        if (_config is null)
        {
            Initialize();
        }

        var created = CreateOriginalDesktopShortcutsInManagedRealms();
        Config.InitialDesktopImportPromptCompleted = true;
        Config.InitialDesktopImportMoveFiles = false;
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

    public void ImportOriginalDesktopToVirtualDesktop(Guid targetDesktopId, bool linkOriginalDesktop, bool saveLayout)
    {
        if (_config is null)
        {
            Initialize();
        }

        if (!linkOriginalDesktop)
        {
            throw new InvalidOperationException(
                "Initial Desktop import refused: DeskRealm no longer moves files from the original Desktop. " +
                "The supported mode is associating the original Desktop with a realm.");
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
        Config.InitialDesktopImportMoveFiles = false;
        Config.InitialDesktopImportSaveLayout = saveLayout;
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
            Config.Enabled,
            name,
            guid,
            realm,
            known,
            _lastSwitchAt,
            _lastMessage,
            assignments);
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
        _startupRealmRestorePending = false;
        _lastDesktopId = desktop.Id;
        _lastSwitchAt = DateTimeOffset.Now;
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
                    : $"Startup realm detected, but icon persistence was disabled: {_iconLayoutsDisabledReason}";
                _logger.Info(_lastMessage);
                _logger.Info(
                    $"[PERF] startup existing-realm reconciliation complete: desktop={desktop.Name}, " +
                    $"elapsed={operation.Elapsed.TotalMilliseconds:0.0} ms.");

                if (!startupRestoreReady)
                {
                    throw new InvalidOperationException(
                        "The startup realm was detected, but its icon layout could not be restored. " +
                        "Icon layout persistence is disabled for this session. Reason: " + _iconLayoutsDisabledReason);
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
            : $"{desktop.Name} -> {realmPath}; icon persistence disabled for this session: {_iconLayoutsDisabledReason}";
        _logger.Info(_lastMessage);
        _logger.Info(
            $"[PERF] realm reconciliation complete: desktop={desktop.Name}, elapsed={operation.Elapsed.TotalMilliseconds:0.0} ms.");

        if (!iconLayoutReady)
        {
            throw new InvalidOperationException(
                "The realm switch completed, but icon layout persistence failed and was disabled for this session. " +
                "Restart DeskRealm after reviewing the log. Reason: " + _iconLayoutsDisabledReason);
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

            realmName = Config.SyncRealmNamesWithVirtualDesktopNames
                ? GetDesiredRealmFolderName(desktop)
                : CreateLegacyRealmName(desktop.Number);

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

        if (Config.SyncRealmNamesWithVirtualDesktopNames)
        {
            realmName = SyncAssignedRealmNameWithDesktopName(desktop, realmName, createIfMissing);
        }

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

        if (string.Equals(currentRealmName, desiredRealmName, StringComparison.OrdinalIgnoreCase))
        {
            return currentRealmName;
        }

        EnsureRealmNameNotAssignedToAnotherDesktop(key, desiredRealmName);

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
        if (!Config.SyncRealmNamesWithVirtualDesktopNames)
        {
            _lastMessage = "Name sync ignored: option disabled.";
            _logger.Info(_lastMessage);
            return;
        }

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
            return true;
        }
        catch (Exception ex)
        {
            DisableIconLayoutsForSession(reason, ex);
            return false;
        }
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

    private string CreateLegacyRealmName(int desktopNumber)
    {
        var realmName = $"D{desktopNumber}";
        while (Config.Assignments.Values.Any(v => !IsAbsoluteRealmAssignment(v) && string.Equals(v, realmName, StringComparison.OrdinalIgnoreCase)))
        {
            realmName = $"D{Config.NextRealmNumber++}";
        }

        return realmName;
    }

    private string GetDesiredRealmFolderName(VirtualDesktopInfo desktop)
    {
        return RealmFolderNameSanitizer.FromVirtualDesktopName(desktop.Name, Config.RealmNameMaxLength);
    }

    private void EnsureRealmNameNotAssignedToAnotherDesktop(string currentKey, string realmName)
    {
        var existing = Config.Assignments.FirstOrDefault(pair =>
            !string.Equals(pair.Key, currentKey, StringComparison.OrdinalIgnoreCase) &&
            !IsAbsoluteRealmAssignment(pair.Value) &&
            string.Equals(pair.Value, realmName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(existing.Key))
        {
            throw new InvalidOperationException(
                $"Realm name already assigned: '{realmName}' is already linked to virtual desktop {existing.Key}. " +
                "DeskRealm does not resolve duplicates silently. Rename one of the desktops in Win+Tab.");
        }
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
            var desiredName = Config.SyncRealmNamesWithVirtualDesktopNames
                ? GetDesiredRealmFolderName(desktop)
                : $"D{desktop.Number}";
            var assignmentKind = rawAssignment != "—" && IsAbsoluteRealmAssignment(rawAssignment) ? "external-path" : "managed-folder";
            return $"  #{desktop.Number} {desktop.Name} {key} -> {realmName} ({assignmentKind}, desired: {desiredName})";
        }));
    }

    private void EnsureMinimumRealms(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            Directory.CreateDirectory(Path.Combine(Config.RealmsRoot!, $"D{i}"));
        }

        if (Config.NextRealmNumber <= count)
        {
            Config.NextRealmNumber = count + 1;
            _configService.Save(Config);
        }
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
    string RealmKey,
    int RealmNumber,
    string RealmName,
    string RealmPath,
    bool IsLocked,
    bool ContainsCurrent,
    IReadOnlyList<IconLayoutEntrySnapshot> Layouts);

internal sealed record IconLayoutEntrySnapshot(
    Guid DesktopId,
    int DesktopNumber,
    string DesktopName,
    bool IsCurrent,
    bool IsLayoutLocked,
    bool EffectiveLocked,
    bool HasSavedLayout,
    IReadOnlyList<IconLayoutVariantSnapshot> Variants);

internal sealed record IconLayoutVariantSnapshot(
    string VariantKey,
    string DisplayTopologyKey,
    string DisplayTopologyFamilyKey,
    string Summary,
    DateTimeOffset? SavedAt,
    int IconCount,
    IReadOnlyList<IconLayoutDisplayFileSnapshot> Displays,
    bool IsCurrentTopology,
    bool IsVariantLocked,
    bool EffectiveLocked,
    bool HasSavedLayout);

internal sealed class IconLayoutRealmBuilder
{
    public IconLayoutRealmBuilder(string realmKey, string realmName, string realmPath, int realmNumber)
    {
        RealmKey = realmKey;
        RealmName = realmName;
        RealmPath = realmPath;
        RealmNumber = realmNumber;
    }

    public string RealmKey { get; }
    public string RealmName { get; }
    public string RealmPath { get; }
    public int RealmNumber { get; }
    public List<IconLayoutEntrySnapshot> Layouts { get; } = [];
}
