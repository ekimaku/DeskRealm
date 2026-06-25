using System.Text.Json;

namespace DeskRealm.App.Services;

internal sealed class RealmConfigService
{
    private readonly FileLogger _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public RealmConfigService(FileLogger logger)
    {
        _logger = logger;
        Directory.CreateDirectory(AppPaths.AppDataRoot);
    }

    public RealmConfig LoadOrCreate(string currentDesktopPath)
    {
        if (!File.Exists(AppPaths.ConfigPath))
        {
            var initial = new RealmConfig
            {
                OriginalDesktopPath = currentDesktopPath,
                RealmsRoot = Path.Combine(currentDesktopPath, "DeskRealm"),
                Enabled = true,
                InitialDesktopImportPromptCompleted = false
            };

            Save(initial);
            _logger.Info($"Config created: {AppPaths.ConfigPath}");
            return initial;
        }

        var raw = File.ReadAllText(AppPaths.ConfigPath);
        var config = JsonSerializer.Deserialize<RealmConfig>(raw, _jsonOptions)
            ?? throw new InvalidOperationException("Unreadable config JSON: empty deserialization.");

        if (config.Version > RealmConfig.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Config schema v{config.Version} is newer than this DeskRealm build (supports up to v{RealmConfig.CurrentVersion}). " +
                "Use a compatible DeskRealm version instead of downgrading the config implicitly.");
        }

        if (string.IsNullOrWhiteSpace(config.OriginalDesktopPath))
        {
            config.OriginalDesktopPath = currentDesktopPath;
            _logger.Warn($"originalDesktopPath missing: initialized to {currentDesktopPath}");
        }

        if (string.IsNullOrWhiteSpace(config.RealmsRoot))
        {
            config.RealmsRoot = Path.Combine(config.OriginalDesktopPath, "DeskRealm");
            _logger.Warn($"realmsRoot missing: initialized to {config.RealmsRoot}");
        }

        if (config.Version < 2)
        {
            config.Version = 2;
            _logger.Warn("Config migration v2: legacy periodic icon polling settings retired.");
        }

        if (config.Version < 3)
        {
            config.IconLayoutDisplayTopologyGuardEnabled = true;
            config.Version = 3;
            _logger.Warn("Config migration v3: display topology/DPI guard enabled to prevent contaminated saves across monitors/resolution/scale.");
        }

        if (config.Version < 4)
        {
            config.Version = 4;
            _logger.Warn("Config migration v4: legacy delayed icon restore settings detected. They are superseded by the v0.6 adaptive readiness pipeline.");
        }

        if (config.Version < 5)
        {
            // Existing installs must not be interrupted by a first-run import wizard after upgrade.
            // New configs are created directly at v5 with InitialDesktopImportPromptCompleted=false.
            config.InitialDesktopImportPromptCompleted = true;
            config.InitialDesktopImportPromptEnabled = true;
            config.Version = 5;
            _logger.Warn("Config migration v5: initial Desktop import assistant available only for new installations.");
        }

        if (config.Version < 6)
        {
            config.Version = 6;
            _logger.Warn("Config migration v6: safe initial Desktop import. DeskRealm associates the original Desktop without moving files.");
        }

        if (config.Version < 7)
        {
            config.LegacyDesktopHotkeys ??= RealmConfig.CreateLegacyDefaultDesktopHotkeys();
            if (UsesLegacyDefaultDesktopHotkeys(config.LegacyDesktopHotkeys))
            {
                config.LegacyDesktopHotkeys = RealmConfig.CreateLegacyDefaultDesktopHotkeys();
                _logger.Warn("Config migration v7: default hotkeys changed to Win+Shift+X/C/B/N to avoid Win+Shift+W/V.");
            }
            else
            {
                _logger.Warn("Config migration v7: custom hotkeys preserved for GUID migration.");
            }

            config.Version = 7;
        }

        if (config.Version < 8)
        {
            config.LockedIconLayouts = NormalizeLockDictionary(config.LockedIconLayouts);
            config.LockedRealms = NormalizeLockDictionary(config.LockedRealms);
            config.Version = 8;
            _logger.Warn("Config migration v8: locked layouts and locked realms support added.");
        }

        if (config.Version < 9)
        {
            config.LockedRealms = MigrateRealmLocksToRealmPathKeys(config);
            config.Version = 9;
            _logger.Warn("Config migration v9: realm locks now use normalized realm path keys so one realm lock protects every assigned layout under that realm.");
        }

        if (config.Version < 10)
        {
            config.LockedIconLayoutVariants = NormalizeLockDictionary(config.LockedIconLayoutVariants);
            config.Version = 10;
            _logger.Warn("Config migration v10: icon layout variant locks added. Variant rows are keyed by virtual desktop GUID + display topology key.");
        }

        if (config.Version < 11)
        {
            config.ShellViewReadyTimeoutMs = 2500;
            config.IconLayoutRestoreVerificationTimeoutMs = 1400;
            config.HotkeyModifierReleaseTimeoutMs = 1200;
            config.DesktopStepConfirmationTimeoutMs = 1800;
            config.Version = 11;
            _logger.Warn(
                "Config migration v11: fixed switch/restore delays were retired. " +
                "DeskRealm now uses registry notifications, modifier release checks, per-step GUID confirmation and adaptive Shell verification.");
        }

        config.Assignments = NormalizeAssignments(config.Assignments);
        config.LockedIconLayouts = NormalizeLockDictionary(config.LockedIconLayouts);
        config.LockedRealms = NormalizeLockDictionary(config.LockedRealms);
        config.LockedIconLayoutVariants = NormalizeLockDictionary(config.LockedIconLayoutVariants);
        config.RealmHotkeys = NormalizeRealmHotkeys(config.RealmHotkeys);
        config.RealmProfiles = NormalizeRealmProfiles(config.RealmProfiles);
        config.RealmWallpapers = NormalizeRealmWallpapers(config.RealmWallpapers);
        config.ArchivedRealmProfiles = NormalizeArchivedRealmProfiles(config.ArchivedRealmProfiles);


        if (config.RealmNameMaxLength is < 16 or > 120)
        {
            throw new InvalidOperationException("realmNameMaxLength invalid. Strict allowed value: 16 to 120 characters.");
        }

        if (config.IconLayoutWorkerTimeoutMs is < 1000 or > 60000)
        {
            throw new InvalidOperationException("iconLayoutWorkerTimeoutMs invalid. Strict allowed value: 1000 to 60000 ms.");
        }

        if (config.ShellViewReadyTimeoutMs is < 250 or > 15000)
        {
            throw new InvalidOperationException("shellViewReadyTimeoutMs invalid. Strict allowed value: 250 to 15000 ms.");
        }

        if (config.IconLayoutRestoreVerificationTimeoutMs is < 250 or > 10000)
        {
            throw new InvalidOperationException("iconLayoutRestoreVerificationTimeoutMs invalid. Strict allowed value: 250 to 10000 ms.");
        }

        if (config.HotkeyModifierReleaseTimeoutMs is < 100 or > 5000)
        {
            throw new InvalidOperationException("hotkeyModifierReleaseTimeoutMs invalid. Strict allowed value: 100 to 5000 ms.");
        }

        if (config.DesktopStepConfirmationTimeoutMs is < 250 or > 10000)
        {
            throw new InvalidOperationException("desktopStepConfirmationTimeoutMs invalid. Strict allowed value: 250 to 10000 ms.");
        }

        if (!Enum.IsDefined(config.RealmRenameApplyMode))
        {
            throw new InvalidOperationException("realmRenameApplyMode invalid. Strict allowed values: Ask, RestartExplorer, NextReboot.");
        }

        if (config.DesktopHotkeysEnabled)
        {
            if (config.Version < 12)
            {
                ValidateLegacyDesktopHotkeys(config);
            }
            else
            {
                ValidateRealmHotkeys(config);
            }
        }

        ValidateRealmProfiles(config);
        ValidateRealmWallpapers(config);
        ValidateArchivedRealmProfiles(config);
        ValidateLockDictionary(config.LockedIconLayouts, "lockedIconLayouts");
        ValidateRealmLockDictionary(config.LockedRealms, "lockedRealms");
        ValidateVariantLockDictionary(config.LockedIconLayoutVariants, "lockedIconLayoutVariants");

        // A config below v12 still needs Windows desktop GUIDs to migrate its
        // number-based hotkeys. DesktopSwitchService performs that binding and
        // saves the canonical config immediately afterwards; saving here would
        // discard the one-time import payload before it can be mapped.
        if (config.Version >= 12)
        {
            Save(config);
        }
        return config;
    }


    private static Dictionary<string, string> NormalizeAssignments(Dictionary<string, string>? assignments)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (assignments is null)
        {
            return normalized;
        }

        foreach (var pair in assignments)
        {
            if (!Guid.TryParse(pair.Key, out var desktopId))
            {
                throw new InvalidOperationException($"assignments contains an invalid Windows desktop GUID key: '{pair.Key}'.");
            }

            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                throw new InvalidOperationException($"assignments[{pair.Key}] is empty.");
            }

            var key = desktopId.ToString("B");
            var assignment = pair.Value.Trim();
            if (normalized.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing, assignment, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"assignments contains conflicting representations of Windows desktop GUID {desktopId:B}.");
                }

                continue;
            }

            normalized.Add(key, assignment);
        }

        return normalized;
    }

    private static Dictionary<string, string> NormalizeRealmHotkeys(Dictionary<string, string>? hotkeys)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (hotkeys is null)
        {
            return normalized;
        }

        foreach (var pair in hotkeys)
        {
            if (!Guid.TryParse(pair.Key, out var desktopId))
            {
                throw new InvalidOperationException($"realmHotkeys contains an invalid Windows desktop GUID key: '{pair.Key}'.");
            }
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                throw new InvalidOperationException($"realmHotkeys[{pair.Key}] is empty.");
            }

            var key = desktopId.ToString("D");
            if (!normalized.TryAdd(key, pair.Value.Trim()))
            {
                throw new InvalidOperationException($"realmHotkeys contains duplicate representations of Windows desktop GUID {desktopId:B}.");
            }
        }

        return normalized;
    }

    private static Dictionary<string, RealmProfile> NormalizeRealmProfiles(Dictionary<string, RealmProfile>? profiles)
    {
        var normalized = new Dictionary<string, RealmProfile>(StringComparer.OrdinalIgnoreCase);
        if (profiles is null)
        {
            return normalized;
        }

        foreach (var pair in profiles)
        {
            if (!Guid.TryParse(pair.Key, out var desktopId))
            {
                throw new InvalidOperationException($"realmProfiles contains an invalid Windows desktop GUID key: '{pair.Key}'.");
            }
            if (pair.Value is null)
            {
                throw new InvalidOperationException($"realmProfiles[{pair.Key}] cannot be null.");
            }

            var key = desktopId.ToString("D");
            if (!normalized.TryAdd(key, pair.Value))
            {
                throw new InvalidOperationException($"realmProfiles contains duplicate representations of Windows desktop GUID {desktopId:B}.");
            }
        }

        return normalized;
    }

    private static Dictionary<string, bool> NormalizeLockDictionary(Dictionary<string, bool>? locks)
    {
        if (locks is null)
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in locks)
        {
            if (!pair.Value || string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            normalized[pair.Key.Trim()] = true;
        }

        return normalized;
    }

    private static Dictionary<string, bool> MigrateRealmLocksToRealmPathKeys(RealmConfig config)
    {
        var migrated = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in NormalizeLockDictionary(config.LockedRealms))
        {
            if (Guid.TryParse(pair.Key, out var desktopId) &&
                config.Assignments.TryGetValue(desktopId.ToString("B"), out var assignment) &&
                !string.IsNullOrWhiteSpace(assignment))
            {
                migrated[BuildRealmLockKeyForConfig(config, assignment)] = true;
                continue;
            }

            migrated[pair.Key.Trim()] = true;
        }

        return migrated;
    }

    private static string BuildRealmLockKeyForConfig(RealmConfig config, string assignment)
    {
        var realmPath = Path.IsPathFullyQualified(assignment)
            ? assignment
            : Path.Combine(config.RealmsRoot ?? string.Empty, assignment);

        return Path.GetFullPath(realmPath.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
    }

    private static void ValidateLockDictionary(IReadOnlyDictionary<string, bool> locks, string propertyName)
    {
        foreach (var pair in locks)
        {
            if (!pair.Value)
            {
                continue;
            }

            if (!Guid.TryParse(pair.Key, out _))
            {
                throw new InvalidOperationException($"{propertyName} contains an invalid GUID key: '{pair.Key}'.");
            }
        }
    }

    private static void ValidateRealmLockDictionary(IReadOnlyDictionary<string, bool> locks, string propertyName)
    {
        foreach (var pair in locks)
        {
            if (!pair.Value)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new InvalidOperationException($"{propertyName} contains an empty key.");
            }
        }
    }


    private static void ValidateVariantLockDictionary(IReadOnlyDictionary<string, bool> locks, string propertyName)
    {
        foreach (var pair in locks)
        {
            if (!pair.Value)
            {
                continue;
            }

            var key = pair.Key?.Trim() ?? string.Empty;
            var separatorIndex = key.IndexOf('|');
            if (separatorIndex <= 0 || separatorIndex == key.Length - 1)
            {
                throw new InvalidOperationException($"{propertyName} contains an invalid variant key: '{pair.Key}'. Expected format: {{desktop-guid}}|{{display-topology-key}}.");
            }

            var desktopKey = key[..separatorIndex];
            var topologyKey = key[(separatorIndex + 1)..];
            if (!Guid.TryParse(desktopKey, out _) || string.IsNullOrWhiteSpace(topologyKey))
            {
                throw new InvalidOperationException($"{propertyName} contains an invalid variant key: '{pair.Key}'. Expected format: {{desktop-guid}}|{{display-topology-key}}.");
            }
        }
    }

    private static bool UsesLegacyDefaultDesktopHotkeys(IReadOnlyDictionary<string, string> hotkeys)
    {
        return hotkeys.Count == 6 &&
               hotkeys.TryGetValue("1", out var d1) && string.Equals(d1, "Win+Shift+W", StringComparison.OrdinalIgnoreCase) &&
               hotkeys.TryGetValue("2", out var d2) && string.Equals(d2, "Win+Shift+X", StringComparison.OrdinalIgnoreCase) &&
               hotkeys.TryGetValue("3", out var d3) && string.Equals(d3, "Win+Shift+C", StringComparison.OrdinalIgnoreCase) &&
               hotkeys.TryGetValue("4", out var d4) && string.Equals(d4, "Win+Shift+V", StringComparison.OrdinalIgnoreCase) &&
               hotkeys.TryGetValue("5", out var d5) && string.Equals(d5, "Win+Shift+B", StringComparison.OrdinalIgnoreCase) &&
               hotkeys.TryGetValue("6", out var d6) && string.Equals(d6, "Win+Shift+N", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateLegacyDesktopHotkeys(RealmConfig config)
    {
        var legacyHotkeys = config.LegacyDesktopHotkeys
            ?? throw new InvalidOperationException("desktopHotkeysEnabled=true but legacy desktopHotkeys is missing.");
        if (legacyHotkeys.Count == 0)
        {
            throw new InvalidOperationException("desktopHotkeysEnabled=true but desktopHotkeys is empty.");
        }

        var seenCombos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in legacyHotkeys)
        {
            if (!int.TryParse(pair.Key, out var desktopNumber) || desktopNumber < 1 || desktopNumber > 32)
            {
                throw new InvalidOperationException($"desktopHotkeys contains an invalid desktop key : '{pair.Key}'. Expected value: 1 to 32.");
            }

            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                throw new InvalidOperationException($"desktopHotkeys[{pair.Key}] is empty.");
            }

            if (!seenCombos.Add(pair.Value.Trim()))
            {
                throw new InvalidOperationException($"Duplicate hotkey shortcut: {pair.Value}.");
            }
        }
    }

    private static void ValidateRealmHotkeys(RealmConfig config)
    {
        var seenCombos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in config.RealmHotkeys)
        {
            if (!Guid.TryParse(pair.Key, out var desktopId))
            {
                throw new InvalidOperationException($"realmHotkeys contains an invalid Windows desktop GUID key: '{pair.Key}'.");
            }

            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                throw new InvalidOperationException($"realmHotkeys[{pair.Key}] is empty.");
            }

            var normalized = HotkeyParser.Parse(desktopId, pair.Value).Text;
            if (!seenCombos.Add(normalized))
            {
                throw new InvalidOperationException($"Duplicate realm hotkey shortcut: {normalized}.");
            }
        }
    }

    private static void ValidateRealmProfiles(RealmConfig config)
    {
        foreach (var pair in config.RealmProfiles)
        {
            if (!Guid.TryParse(pair.Key, out _))
            {
                throw new InvalidOperationException($"realmProfiles contains an invalid Windows desktop GUID key: '{pair.Key}'.");
            }
            if (pair.Value is null)
            {
                throw new InvalidOperationException($"realmProfiles[{pair.Key}] cannot be null.");
            }
        }
    }


    private static Dictionary<string, RealmWallpaper> NormalizeRealmWallpapers(Dictionary<string, RealmWallpaper>? wallpapers)
    {
        var normalized = new Dictionary<string, RealmWallpaper>(StringComparer.OrdinalIgnoreCase);
        if (wallpapers is null) return normalized;
        foreach (var pair in wallpapers)
        {
            if (!Guid.TryParse(pair.Key, out var desktopId))
            {
                throw new InvalidOperationException($"realmWallpapers contains an invalid Windows desktop GUID key: '{pair.Key}'.");
            }
            if (pair.Value is null || string.IsNullOrWhiteSpace(pair.Value.ManagedPath))
            {
                throw new InvalidOperationException($"realmWallpapers[{pair.Key}] must contain a managedPath.");
            }
            if (!normalized.TryAdd(desktopId.ToString("D"), pair.Value))
            {
                throw new InvalidOperationException($"realmWallpapers contains duplicate representations of Windows desktop GUID {desktopId:B}.");
            }
        }
        return normalized;
    }

    private static Dictionary<string, ArchivedRealmProfile> NormalizeArchivedRealmProfiles(Dictionary<string, ArchivedRealmProfile>? archives)
    {
        var normalized = new Dictionary<string, ArchivedRealmProfile>(StringComparer.OrdinalIgnoreCase);
        if (archives is null) return normalized;
        foreach (var pair in archives)
        {
            var name = pair.Key?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("archivedRealmProfiles contains an empty realm-name key.");
            if (pair.Value is null || !Guid.TryParse(pair.Value.SourceDesktopId, out _))
            {
                throw new InvalidOperationException($"archivedRealmProfiles['{pair.Key}'] must contain a valid sourceDesktopId.");
            }
            normalized[name] = pair.Value;
        }
        return normalized;
    }

    private static void ValidateRealmWallpapers(RealmConfig config)
    {
        foreach (var pair in config.RealmWallpapers)
        {
            if (!Guid.TryParse(pair.Key, out _)) throw new InvalidOperationException($"realmWallpapers contains an invalid Windows desktop GUID key: '{pair.Key}'.");
            if (pair.Value is null || string.IsNullOrWhiteSpace(pair.Value.ManagedPath)) throw new InvalidOperationException($"realmWallpapers[{pair.Key}] must contain a managedPath.");
        }
    }

    private static void ValidateArchivedRealmProfiles(RealmConfig config)
    {
        foreach (var pair in config.ArchivedRealmProfiles)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)) throw new InvalidOperationException("archivedRealmProfiles contains an empty realm-name key.");
            if (pair.Value is null || !Guid.TryParse(pair.Value.SourceDesktopId, out _)) throw new InvalidOperationException($"archivedRealmProfiles['{pair.Key}'] must contain a valid sourceDesktopId.");
        }
    }

    public void Save(RealmConfig config)
    {
        Directory.CreateDirectory(AppPaths.AppDataRoot);
        var raw = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(AppPaths.ConfigPath, raw);
    }
}
