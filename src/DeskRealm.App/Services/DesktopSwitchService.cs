namespace DeskRealm.App.Services;

internal sealed class DesktopSwitchService
{
    private readonly RealmConfigService _configService;
    private readonly KnownFolderService _knownFolder;
    private readonly VirtualDesktopRegistryService _virtualDesktop;
    private readonly ShellRefreshService _shellRefresh;
    private readonly IconLayoutWorkerClientService _iconLayouts;
    private readonly VirtualDesktopNavigatorService _navigator;
    private readonly FileLogger _logger;

    private RealmConfig? _config;
    private Guid? _lastDesktopId;
    private string _lastMessage = "Initialisé.";
    private DateTimeOffset _lastSwitchAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastIconAutoSaveAt = DateTimeOffset.MinValue;
    private string? _lastDisplayTopologyKey;
    private DateTimeOffset _lastDisplayTopologyChangedAt = DateTimeOffset.MinValue;
    private bool _displayTopologyRestorePending;
    private Guid? _pendingIconRestoreDesktopId;
    private string? _pendingIconRestoreDesktopName;
    private string? _pendingIconRestoreRealmPath;
    private string? _pendingIconRestoreRealmName;
    private DateTimeOffset _pendingIconRestoreReadyAt = DateTimeOffset.MinValue;
    private string? _pendingIconRestoreReason;
    private bool _iconLayoutsDisabledForSession;
    private string? _iconLayoutsDisabledReason;

    public DesktopSwitchService(
        RealmConfigService configService,
        KnownFolderService knownFolder,
        VirtualDesktopRegistryService virtualDesktop,
        ShellRefreshService shellRefresh,
        IconLayoutWorkerClientService iconLayouts,
        VirtualDesktopNavigatorService navigator,
        FileLogger logger)
    {
        _configService = configService;
        _knownFolder = knownFolder;
        _virtualDesktop = virtualDesktop;
        _shellRefresh = shellRefresh;
        _iconLayouts = iconLayouts;
        _navigator = navigator;
        _logger = logger;
    }

    public RealmConfig Config => _config ?? throw new InvalidOperationException("Config non initialisée.");

    public void Initialize()
    {
        var currentDesktopPath = _knownFolder.GetDesktopPath();
        _config = _configService.LoadOrCreate(currentDesktopPath);

        if (Config.IconLayoutDisplayTopologyGuardEnabled)
        {
            var topology = DisplayTopologyService.Capture();
            _lastDisplayTopologyKey = topology.Key;
            _lastDisplayTopologyChangedAt = DateTimeOffset.Now;
            _logger.Info($"Initial display topology captured: {topology.Key} ({topology.Screens.Count} screen(s), virtual={topology.VirtualBoundsWidth}x{topology.VirtualBoundsHeight}).");
        }

        if (Config.RejectOneDriveDesktop && ContainsOneDriveSegment(Config.OriginalDesktopPath!))
        {
            throw new InvalidOperationException(
                "Desktop original détecté sous OneDrive. DeskRealm refuse ce mode par défaut. " +
                "Désactive rejectOneDriveDesktop uniquement si tu veux assumer ce risque explicitement.");
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

        _lastMessage = "Config chargée.";
        _logger.Info($"Original Desktop: {Config.OriginalDesktopPath}");
        _logger.Info($"Realms root: {Config.RealmsRoot}");
        _logger.Info($"Realm name sync: {Config.SyncRealmNamesWithVirtualDesktopNames}");
        _logger.Info($"Icon layout persistence: {Config.IconLayoutPersistenceEnabled}");
        _logger.Info($"Icon layout background autosave: {Config.IconLayoutAutoSaveEnabled} / {Config.IconLayoutAutoSaveIntervalMs} ms");
        if (!Config.IconLayoutAutoSaveEnabled)
        {
            _logger.Info("Icon layout background autosave disabled. Layouts are saved on desktop switch, manual save, and exit restore.");
        }
        _logger.Info($"Icon layout worker timeout: {Config.IconLayoutWorkerTimeoutMs} ms");
        _logger.Info($"Icon layout delayed switch restore: delay={Config.IconLayoutSwitchRestoreDelayMs} ms, retries={Config.IconLayoutRestoreRetryCount}, retryDelay={Config.IconLayoutRestoreRetryDelayMs} ms");
        _logger.Info($"Desktop hotkeys: {Config.DesktopHotkeysEnabled} / {string.Join(", ", Config.DesktopHotkeys.Select(p => $"#{p.Key}={p.Value}"))}");
        _logger.Info($"Hotkey switch timing: initial={Config.HotkeyInitialDelayMs} ms, step={Config.HotkeySwitchStepDelayMs} ms, settle={Config.HotkeySwitchSettleTimeoutMs} ms");
    }

    public void Tick()
    {
        if (_config is null)
        {
            Initialize();
        }

        if (!Config.Enabled)
        {
            _lastMessage = "Pause active.";
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
            RestoreIconLayoutAfterDisplayTopologySettled(current, realmPath);
            TryRestorePendingIconLayout(current, realmPath, currentKnownDesktop, "stable-tick");
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

        var desktops = _virtualDesktop.GetVirtualDesktops();
        var target = desktops.FirstOrDefault(d => d.Number == targetNumber)
            ?? throw new InvalidOperationException($"Bureau virtuel #{targetNumber} introuvable. Bureaux disponibles : 1 à {desktops.Count}.");

        var current = _virtualDesktop.GetCurrentVirtualDesktop();
        if (current.Id == target.Id)
        {
            var currentRealmPath = ResolveRealmPath(current, createIfMissing: true);
            SwitchTo(current, currentRealmPath, force: false);
            _lastMessage = $"Hotkey bureau #{targetNumber} ignoré : déjà sur {current.Name}.";
            _logger.Info(_lastMessage);
            return;
        }

        SaveIconLayoutForKnownDesktopIfRealm(_knownFolder.GetDesktopPath());
        _navigator.NavigateByNumber(current.Number, targetNumber, desktops.Count, Config.HotkeySwitchStepDelayMs);

        var switched = WaitForCurrentDesktop(target.Id, Config.HotkeySwitchSettleTimeoutMs);
        if (switched.Id != target.Id)
        {
            throw new InvalidOperationException(
                $"Windows n'a pas confirmé le switch vers le bureau #{targetNumber} dans le délai attendu. " +
                $"Bureau courant détecté : #{switched.Number} {switched.Name} {switched.Id:B}.");
        }

        var realmPath = ResolveRealmPath(switched, createIfMissing: true);
        SwitchTo(switched, realmPath, force: true);
        _lastMessage = $"Hotkey -> bureau #{targetNumber} {switched.Name}.";
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
        if (_config is null)
        {
            Initialize();
        }

        Config.InitialDesktopImportPromptCompleted = true;
        _configService.Save(Config);
        _logger.Info("Initial Desktop import skipped by user.");
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
                "Import Desktop initial refusé : DeskRealm ne déplace plus les fichiers du Desktop original. " +
                "Le mode supporté est l'association du Desktop original à un realm.");
        }

        var originalDesktop = Config.OriginalDesktopPath
            ?? throw new InvalidOperationException("originalDesktopPath absent dans la config.");

        if (!Directory.Exists(originalDesktop))
        {
            throw new DirectoryNotFoundException($"Desktop original introuvable : {originalDesktop}");
        }

        var knownDesktop = _knownFolder.GetDesktopPath();
        if (!PathsEqual(knownDesktop, originalDesktop))
        {
            throw new InvalidOperationException(
                "Import Desktop initial refusé : le Desktop connu actif n'est plus le Desktop original. " +
                $"Attendu : {originalDesktop}. Actuel : {knownDesktop}.");
        }

        var targetDesktop = _virtualDesktop.GetVirtualDesktops().FirstOrDefault(d => d.Id == targetDesktopId)
            ?? throw new InvalidOperationException($"Bureau virtuel cible introuvable : {targetDesktopId:B}");

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
        _lastMessage = $"Desktop original associé à {targetDesktop.Name} sans déplacement de fichiers.";
        _logger.Info(
            $"Initial Desktop import completed: target={targetDesktop.Name} {targetDesktop.Id:B}, " +
            $"mode=link-original-desktop, original={originalDesktop}, previousAssignment={previousAssignment}, saveLayout={saveLayout}.");
    }

    public void SaveIconLayoutNow()
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
                "Impossible de sauvegarder le layout icônes : le Desktop connu actif ne correspond à aucun realm DeskRealm assigné. " +
                $"Desktop actif : {knownDesktop}");
        }

        EnsureKnownDesktopAssignmentIsCurrentDesktop(desktopId, realmName, "manual-save");
        EnsureIconLayoutsNotDisabledForSession();
        _iconLayouts.Save(desktopId, realmName, Config.IconLayoutWorkerTimeoutMs);
        _lastIconAutoSaveAt = DateTimeOffset.Now;
        _lastMessage = $"Layout icônes sauvegardé : {realmName}";
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
                "Impossible de restaurer le layout icônes : le Desktop connu actif n'est pas le realm du bureau virtuel courant. " +
                $"Attendu : {realmPath}. Actuel : {knownDesktop}. Lance d'abord Refresh now.");
        }

        EnsureIconLayoutsNotDisabledForSession();
        if (Config.IconLayoutSettleDelayMs > 0)
        {
            Thread.Sleep(Config.IconLayoutSettleDelayMs);
        }

        var realmName = Path.GetFileName(realmPath);
        _iconLayouts.Restore(current.Id, realmName, Config.IconLayoutWorkerTimeoutMs);
        _lastMessage = $"Layout icônes restauré : {Path.GetFileName(realmPath)}";
        _logger.Info(_lastMessage);
    }

    public void SetEnabled(bool enabled)
    {
        Config.Enabled = enabled;
        _configService.Save(Config);
        _lastMessage = enabled ? "Reprise active." : "Pause active.";
        _logger.Info(_lastMessage);
    }

    public void RestoreOriginalDesktop()
    {
        SaveIconLayoutForKnownDesktopIfRealm(_knownFolder.GetDesktopPath(), "restore-original/save-before-restore");

        var original = Config.OriginalDesktopPath
            ?? throw new InvalidOperationException("originalDesktopPath absent dans la config.");

        if (!Directory.Exists(original))
        {
            throw new DirectoryNotFoundException($"Desktop original introuvable : {original}");
        }

        _knownFolder.SetDesktopPath(original);
        _shellRefresh.RefreshDesktop(original);
        ClearPendingIconRestore();
        _lastDesktopId = null;
        _lastSwitchAt = DateTimeOffset.Now;
        _lastMessage = $"Desktop original restauré : {original}";
        _logger.Info(_lastMessage);
    }



    private void EnsureInitialImportCanMove(string originalDesktop, string targetRealmPath, bool moveFiles)
    {
        if (!moveFiles)
        {
            return;
        }

        foreach (var source in EnumerateInitialDesktopImportCandidates(originalDesktop, targetRealmPath))
        {
            var target = Path.Combine(targetRealmPath, Path.GetFileName(source));
            if (File.Exists(target) || Directory.Exists(target))
            {
                throw new IOException(
                    "Import Desktop initial refusé : conflit de nom détecté dans le realm cible. " +
                    $"Source : {source}. Cible existante : {target}. " +
                    "Renomme ou déplace l'élément manuellement, puis relance l'import.");
            }
        }
    }

    private int MoveInitialDesktopItems(string originalDesktop, string targetRealmPath)
    {
        Directory.CreateDirectory(targetRealmPath);
        var moved = 0;
        foreach (var source in EnumerateInitialDesktopImportCandidates(originalDesktop, targetRealmPath))
        {
            var target = Path.Combine(targetRealmPath, Path.GetFileName(source));
            if (Directory.Exists(source))
            {
                Directory.Move(source, target);
            }
            else
            {
                File.Move(source, target);
            }

            moved++;
            _logger.Info($"Initial Desktop import moved: {source} -> {target}");
        }

        return moved;
    }

    private IEnumerable<string> EnumerateInitialDesktopImportCandidates(string originalDesktop, string targetRealmPath)
    {
        var realmsRoot = Config.RealmsRoot ?? string.Empty;
        foreach (var entry in Directory.EnumerateFileSystemEntries(originalDesktop))
        {
            var name = Path.GetFileName(entry);
            if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info($"Initial Desktop import skips desktop.ini: {entry}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(realmsRoot) && PathsEqual(entry, realmsRoot))
            {
                _logger.Info($"Initial Desktop import skips DeskRealm realms root: {entry}");
                continue;
            }

            if (PathsEqual(entry, targetRealmPath))
            {
                _logger.Info($"Initial Desktop import skips target realm path: {entry}");
                continue;
            }

            yield return entry;
        }
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

    private VirtualDesktopInfo WaitForCurrentDesktop(Guid expectedDesktopId, int timeoutMs)
    {
        var deadline = DateTimeOffset.Now.AddMilliseconds(timeoutMs);
        Exception? lastError = null;

        while (DateTimeOffset.Now <= deadline)
        {
            try
            {
                var current = _virtualDesktop.GetCurrentVirtualDesktop();
                if (current.Id == expectedDesktopId)
                {
                    return current;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            Thread.Sleep(80);
        }

        if (lastError is not null)
        {
            _logger.Warn($"Hotkey settle wait had registry read errors: {lastError.Message}");
        }

        return _virtualDesktop.GetCurrentVirtualDesktop();
    }

    private void SwitchTo(VirtualDesktopInfo desktop, string realmPath, bool force = false)
    {
        var currentKnownDesktop = _knownFolder.GetDesktopPath();

        if (!force && string.Equals(currentKnownDesktop, realmPath, StringComparison.OrdinalIgnoreCase))
        {
            _lastDesktopId = desktop.Id;
            _lastMessage = $"Déjà sur {desktop.Name} -> {Path.GetFileName(realmPath)}";
            return;
        }

        SaveIconLayoutForKnownDesktopIfRealm(currentKnownDesktop);

        _knownFolder.SetDesktopPath(realmPath);
        _shellRefresh.RefreshDesktop(realmPath);
        ScheduleIconLayoutRestore(desktop, realmPath, "switch");

        _lastDesktopId = desktop.Id;
        _lastSwitchAt = DateTimeOffset.Now;
        _lastMessage = $"{desktop.Name} -> {realmPath}";
        _logger.Info(_lastMessage);
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
                throw new DirectoryNotFoundException($"Realm externe absent : {realmName}");
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
            throw new DirectoryNotFoundException($"Realm absent : {realmPath}");
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
                "Conflit de renommage realm. " +
                $"DeskRealm voulait renommer '{currentPath}' vers '{desiredPath}', mais le dossier cible existe déjà. " +
                "Renomme ou fusionne manuellement un des deux dossiers, puis relance Sync names now.");
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
        _lastMessage = $"Nom synchronisé : {desktop.Name} -> {desiredRealmName}";
        return desiredRealmName;
    }

    private void SyncAllRealmFolderNames(bool createIfMissing, bool reswitchCurrentDesktop)
    {
        if (!Config.SyncRealmNamesWithVirtualDesktopNames)
        {
            _lastMessage = "Sync noms ignoré : option désactivée.";
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

        _lastMessage = "Noms des realms synchronisés avec Win+Tab.";
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
            ?? throw new InvalidOperationException("originalDesktopPath absent dans la config.");

        if (!Directory.Exists(original))
        {
            throw new DirectoryNotFoundException($"Desktop original introuvable : {original}");
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
            _lastDisplayTopologyChangedAt = DateTimeOffset.Now;
            _logger.Info($"Display topology baseline established ({reason}): {topology.Key}.");
            return;
        }

        if (string.Equals(_lastDisplayTopologyKey, topology.Key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastDisplayTopologyKey = topology.Key;
        _lastDisplayTopologyChangedAt = DateTimeOffset.Now;
        _displayTopologyRestorePending = true;
        _lastMessage = "Topologie écran modifiée : restauration layout icônes en attente.";
        _logger.Warn(
            $"Display topology changed ({reason}): {topology.Key} " +
            $"({topology.Screens.Count} screen(s), virtual={topology.VirtualBoundsWidth}x{topology.VirtualBoundsHeight}). " +
            "Icon layout saves are guarded until settle, then current realm layout will be restored.");
    }

    private bool IsDisplayTopologySaveGuardActive(string reason)
    {
        if (!Config.IconLayoutDisplayTopologyGuardEnabled || !_displayTopologyRestorePending)
        {
            return false;
        }

        var elapsedMs = (DateTimeOffset.Now - _lastDisplayTopologyChangedAt).TotalMilliseconds;
        if (elapsedMs < Config.IconLayoutDisplayTopologySettleDelayMs)
        {
            _logger.Info(
                $"Icon layout {reason} skipped: display topology changed {elapsedMs:0} ms ago; " +
                $"waiting {Config.IconLayoutDisplayTopologySettleDelayMs} ms before accepting saves.");
            return true;
        }

        if (_displayTopologyRestorePending)
        {
            _logger.Info($"Icon layout {reason} skipped: display topology restore is pending. Restore first to avoid saving Windows-compacted icon positions.");
            return true;
        }

        return false;
    }

    private void RestoreIconLayoutAfterDisplayTopologySettled(VirtualDesktopInfo current, string realmPath)
    {
        if (!Config.IconLayoutDisplayTopologyGuardEnabled || !_displayTopologyRestorePending)
        {
            return;
        }

        var elapsedMs = (DateTimeOffset.Now - _lastDisplayTopologyChangedAt).TotalMilliseconds;
        if (elapsedMs < Config.IconLayoutDisplayTopologySettleDelayMs)
        {
            return;
        }

        _logger.Info($"Display topology settled after {elapsedMs:0} ms. Restoring current realm icon layout before any future save.");
        RestoreIconLayoutForDesktop(current, realmPath);
        _displayTopologyRestorePending = false;
        _lastMessage = "Topologie écran stabilisée : layout icônes restauré.";
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

        if (IsIconLayoutSwitchRestorePending(reason))
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

            _iconLayouts.SaveIfChanged(desktopId, realmName, Config.IconLayoutWorkerTimeoutMs);
            _lastIconAutoSaveAt = DateTimeOffset.Now;
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
            "Sauvegarde layout icônes refusée : le Desktop connu actif appartient au realm " +
            $"'{realmName}' {assignedDesktopId:B}, mais le bureau virtuel Windows courant est " +
            $"'{currentDesktop.Name}' {currentDesktop.Id:B}. " +
            "Attends que DeskRealm ait terminé le switch, puis utilise Save icon layout now depuis le realm actif.");
    }

    private void ScheduleIconLayoutRestore(VirtualDesktopInfo desktop, string realmPath, string reason)
    {
        if (!Config.IconLayoutPersistenceEnabled)
        {
            return;
        }

        var realmName = Path.GetFileName(realmPath);
        var delayMs = Math.Max(0, Config.IconLayoutSwitchRestoreDelayMs);
        _pendingIconRestoreDesktopId = desktop.Id;
        _pendingIconRestoreDesktopName = desktop.Name;
        _pendingIconRestoreRealmPath = realmPath;
        _pendingIconRestoreRealmName = realmName;
        _pendingIconRestoreReadyAt = DateTimeOffset.Now.AddMilliseconds(delayMs);
        _pendingIconRestoreReason = reason;
        _logger.Info($"Icon layout restore scheduled after switch: {realmName} {desktop.Id:B}, delay={delayMs} ms, reason={reason}.");
    }

    private bool IsIconLayoutSwitchRestorePending(string reason)
    {
        if (!_pendingIconRestoreDesktopId.HasValue)
        {
            return false;
        }

        var remainingMs = (_pendingIconRestoreReadyAt - DateTimeOffset.Now).TotalMilliseconds;
        _logger.Info(
            $"Icon layout {reason} skipped: switch restore pending for '{_pendingIconRestoreRealmName}' " +
            $"{_pendingIconRestoreDesktopId.Value:B}; restore in {Math.Max(0, remainingMs):0} ms. " +
            "Skipping save to avoid capturing transient icons from the previous realm.");
        return true;
    }

    private void TryRestorePendingIconLayout(VirtualDesktopInfo current, string realmPath, string currentKnownDesktop, string reason)
    {
        if (!_pendingIconRestoreDesktopId.HasValue)
        {
            return;
        }

        if (current.Id != _pendingIconRestoreDesktopId.Value)
        {
            _logger.Info(
                $"Pending icon restore kept waiting ({reason}): current desktop is {current.Name} {current.Id:B}, " +
                $"expected {_pendingIconRestoreDesktopName} {_pendingIconRestoreDesktopId.Value:B}.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_pendingIconRestoreRealmPath) ||
            !PathsEqual(currentKnownDesktop, _pendingIconRestoreRealmPath) ||
            !PathsEqual(realmPath, _pendingIconRestoreRealmPath))
        {
            _logger.Info(
                $"Pending icon restore kept waiting ({reason}): Desktop known folder not settled on target realm yet. " +
                $"Known={currentKnownDesktop}, target={_pendingIconRestoreRealmPath}.");
            return;
        }

        var remainingMs = (_pendingIconRestoreReadyAt - DateTimeOffset.Now).TotalMilliseconds;
        if (remainingMs > 0)
        {
            _logger.Info($"Pending icon restore kept waiting ({reason}): Explorer settle delay still active ({remainingMs:0} ms remaining).");
            return;
        }

        var pendingRealmPath = _pendingIconRestoreRealmPath;
        var pendingReason = _pendingIconRestoreReason ?? reason;

        ClearPendingIconRestore();
        RestoreIconLayoutForDesktop(current, pendingRealmPath, $"pending-{pendingReason}");
    }

    private void ClearPendingIconRestore()
    {
        _pendingIconRestoreDesktopId = null;
        _pendingIconRestoreDesktopName = null;
        _pendingIconRestoreRealmPath = null;
        _pendingIconRestoreRealmName = null;
        _pendingIconRestoreReadyAt = DateTimeOffset.MinValue;
        _pendingIconRestoreReason = null;
    }

    private void RestoreIconLayoutForDesktop(VirtualDesktopInfo desktop, string realmPath, string reason = "auto-restore")
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

        if (Config.IconLayoutSettleDelayMs > 0)
        {
            Thread.Sleep(Config.IconLayoutSettleDelayMs);
        }

        var realmName = Path.GetFileName(realmPath);
        var attempts = Math.Clamp(Config.IconLayoutRestoreRetryCount, 1, 5);
        try
        {
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                _iconLayouts.Restore(desktop.Id, realmName, Config.IconLayoutWorkerTimeoutMs);
                _logger.Info($"Icon layout {reason} restore attempt {attempt}/{attempts}: {realmName} {desktop.Id:B}.");

                if (attempt < attempts && Config.IconLayoutRestoreRetryDelayMs > 0)
                {
                    Thread.Sleep(Config.IconLayoutRestoreRetryDelayMs);
                }
            }

            _displayTopologyRestorePending = false;
        }
        catch (Exception ex)
        {
            DisableIconLayoutsForSession(reason, ex);
        }
    }

    private void DisableIconLayoutsForSession(string operation, Exception ex)
    {
        _iconLayoutsDisabledForSession = true;
        _iconLayoutsDisabledReason = $"{operation}: {ex.Message}";
        _lastMessage = "Persistance icônes désactivée pour cette session : " + _iconLayoutsDisabledReason;
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
                throw new InvalidOperationException($"Assignment GUID invalide dans la config : {assignment.Key}");
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
                    "Import Desktop initial refusé : le Desktop original est déjà associé à un autre bureau virtuel. " +
                    $"Assignment existante : {assignment.Key} -> {assignment.Value}.");
            }
        }
    }

    private void EnsureIconLayoutPersistenceEnabled()
    {
        if (!Config.IconLayoutPersistenceEnabled)
        {
            throw new InvalidOperationException("La persistance des positions d'icônes est désactivée dans la config.");
        }
    }

    private void EnsureIconLayoutsNotDisabledForSession()
    {
        if (_iconLayoutsDisabledForSession)
        {
            throw new InvalidOperationException(
                "La persistance des positions d'icônes a été désactivée pour cette session après une erreur worker. " +
                "Redémarre DeskRealm après correction pour réessayer. Dernière erreur : " + _iconLayoutsDisabledReason);
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
                $"Nom de realm déjà assigné : '{realmName}' est déjà lié au bureau virtuel {existing.Key}. " +
                "DeskRealm ne résout pas les doublons silencieusement. Renomme un des bureaux dans Win+Tab.");
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
