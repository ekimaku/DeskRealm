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
        _logger.Info($"Icon layout autosave: {Config.IconLayoutAutoSaveEnabled} / {Config.IconLayoutAutoSaveIntervalMs} ms");
        _logger.Info($"Icon layout worker timeout: {Config.IconLayoutWorkerTimeoutMs} ms");
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

        if (_lastDesktopId.HasValue &&
            _lastDesktopId.Value == current.Id &&
            string.Equals(currentKnownDesktop, realmPath, StringComparison.OrdinalIgnoreCase))
        {
            AutoSaveIconLayoutIfDue(currentKnownDesktop);
            return;
        }

        SwitchTo(current, realmPath);
        AutoSaveIconLayoutIfDue(_knownFolder.GetDesktopPath(), forceDelayReset: true);
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
        _shellRefresh.RefreshDesktop(realmPath);
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
        var original = Config.OriginalDesktopPath
            ?? throw new InvalidOperationException("originalDesktopPath absent dans la config.");

        if (!Directory.Exists(original))
        {
            throw new DirectoryNotFoundException($"Desktop original introuvable : {original}");
        }

        _knownFolder.SetDesktopPath(original);
        _shellRefresh.RefreshDesktop(original);
        _lastDesktopId = null;
        _lastSwitchAt = DateTimeOffset.Now;
        _lastMessage = $"Desktop original restauré : {original}";
        _logger.Info(_lastMessage);
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
        RestoreIconLayoutForDesktop(desktop, realmPath);

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

    private void SaveIconLayoutForKnownDesktopIfRealm(string knownDesktopPath)
    {
        if (!Config.IconLayoutPersistenceEnabled)
        {
            return;
        }

        if (_iconLayoutsDisabledForSession)
        {
            _logger.Warn($"Icon layout auto-save skipped: feature disabled for this session. Reason: {_iconLayoutsDisabledReason}");
            return;
        }

        if (!TryFindAssignmentByRealmPath(knownDesktopPath, out var desktopId, out var realmName))
        {
            _logger.Info($"Icon layout save skipped: active Desktop is not an assigned DeskRealm realm ({knownDesktopPath}).");
            return;
        }

        try
        {
            _iconLayouts.Save(desktopId, realmName, Config.IconLayoutWorkerTimeoutMs);
            _lastIconAutoSaveAt = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            DisableIconLayoutsForSession("auto-save", ex);
        }
    }

    private void AutoSaveIconLayoutIfDue(string knownDesktopPath, bool forceDelayReset = false)
    {
        if (!Config.IconLayoutPersistenceEnabled || !Config.IconLayoutAutoSaveEnabled)
        {
            return;
        }

        if (_iconLayoutsDisabledForSession)
        {
            return;
        }

        if (!TryFindAssignmentByRealmPath(knownDesktopPath, out var desktopId, out var realmName))
        {
            return;
        }

        var now = DateTimeOffset.Now;
        if (forceDelayReset)
        {
            _lastIconAutoSaveAt = now;
            return;
        }

        if ((now - _lastIconAutoSaveAt).TotalMilliseconds < Config.IconLayoutAutoSaveIntervalMs)
        {
            return;
        }

        _lastIconAutoSaveAt = now;
        try
        {
            _iconLayouts.SaveIfChanged(desktopId, realmName, Config.IconLayoutWorkerTimeoutMs);
            _lastIconAutoSaveAt = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            DisableIconLayoutsForSession("auto-save-if-changed", ex);
        }
    }

    private void RestoreIconLayoutForDesktop(VirtualDesktopInfo desktop, string realmPath)
    {
        if (!Config.IconLayoutPersistenceEnabled)
        {
            return;
        }

        if (_iconLayoutsDisabledForSession)
        {
            _logger.Warn($"Icon layout auto-restore skipped: feature disabled for this session. Reason: {_iconLayoutsDisabledReason}");
            return;
        }

        if (Config.IconLayoutSettleDelayMs > 0)
        {
            Thread.Sleep(Config.IconLayoutSettleDelayMs);
        }

        var realmName = Path.GetFileName(realmPath);
        try
        {
            _iconLayouts.Restore(desktop.Id, realmName, Config.IconLayoutWorkerTimeoutMs);
            _shellRefresh.RefreshDesktop(realmPath);
        }
        catch (Exception ex)
        {
            DisableIconLayoutsForSession("auto-restore", ex);
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
            var candidatePath = Path.Combine(Config.RealmsRoot!, assignment.Value);
            if (!PathsEqual(path, candidatePath))
            {
                continue;
            }

            if (!Guid.TryParse(assignment.Key, out desktopId))
            {
                throw new InvalidOperationException($"Assignment GUID invalide dans la config : {assignment.Key}");
            }

            realmName = assignment.Value;
            return true;
        }

        desktopId = Guid.Empty;
        realmName = string.Empty;
        return false;
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
        while (Config.Assignments.Values.Any(v => string.Equals(v, realmName, StringComparison.OrdinalIgnoreCase)))
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
            var realmName = Config.Assignments.TryGetValue(key, out var assignment) ? assignment : "—";
            var desiredName = Config.SyncRealmNamesWithVirtualDesktopNames
                ? GetDesiredRealmFolderName(desktop)
                : $"D{desktop.Number}";
            return $"  #{desktop.Number} {desktop.Name} {key} -> {realmName} (desired: {desiredName})";
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
