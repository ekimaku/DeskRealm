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
                NextRealmNumber = 1,
                Enabled = true,
                InitialDesktopImportPromptCompleted = false,
                InitialDesktopImportMoveFiles = false
            };

            Save(initial);
            _logger.Info($"Config created: {AppPaths.ConfigPath}");
            return initial;
        }

        var raw = File.ReadAllText(AppPaths.ConfigPath);
        var config = JsonSerializer.Deserialize<RealmConfig>(raw, _jsonOptions)
            ?? throw new InvalidOperationException("Unreadable config JSON: empty deserialization.");

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
            config.InitialDesktopImportMoveFiles = true;
            config.InitialDesktopImportSaveLayout = true;
            config.Version = 5;
            _logger.Warn("Config migration v5: initial Desktop import assistant available only for new installations.");
        }

        if (config.Version < 6)
        {
            config.InitialDesktopImportMoveFiles = false;
            config.Version = 6;
            _logger.Warn("Config migration v6: safe initial Desktop import. DeskRealm associates the original Desktop without moving files.");
        }

        if (config.Version < 7)
        {
            if (UsesLegacyDefaultDesktopHotkeys(config.DesktopHotkeys))
            {
                config.DesktopHotkeys = RealmConfig.CreateDefaultDesktopHotkeys();
                _logger.Warn("Config migration v7: default hotkeys changed to Win+Shift+X/C/B/N to avoid Win+Shift+W/V.");
            }
            else
            {
                _logger.Warn("Config migration v7: custom hotkeys preserved.");
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

        config.LockedIconLayouts = NormalizeLockDictionary(config.LockedIconLayouts);
        config.LockedRealms = NormalizeLockDictionary(config.LockedRealms);
        config.LockedIconLayoutVariants = NormalizeLockDictionary(config.LockedIconLayoutVariants);

        if (config.NextRealmNumber < 1)
        {
            throw new InvalidOperationException("nextRealmNumber is invalid. Strict minimum value: 1.");
        }

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

        if (config.DesktopHotkeysEnabled)
        {
            ValidateDesktopHotkeys(config);
        }

        ValidateLockDictionary(config.LockedIconLayouts, "lockedIconLayouts");
        ValidateRealmLockDictionary(config.LockedRealms, "lockedRealms");
        ValidateVariantLockDictionary(config.LockedIconLayoutVariants, "lockedIconLayoutVariants");

        Save(config);
        return config;
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

    private static void ValidateDesktopHotkeys(RealmConfig config)
    {
        if (config.DesktopHotkeys.Count == 0)
        {
            throw new InvalidOperationException("desktopHotkeysEnabled=true but desktopHotkeys is empty.");
        }

        var seenCombos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in config.DesktopHotkeys)
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

    public void Save(RealmConfig config)
    {
        Directory.CreateDirectory(AppPaths.AppDataRoot);
        var raw = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(AppPaths.ConfigPath, raw);
    }
}
