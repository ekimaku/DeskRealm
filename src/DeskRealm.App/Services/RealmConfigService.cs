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
                Enabled = true
            };

            Save(initial);
            _logger.Info($"Config created: {AppPaths.ConfigPath}");
            return initial;
        }

        var raw = File.ReadAllText(AppPaths.ConfigPath);
        var config = JsonSerializer.Deserialize<RealmConfig>(raw, _jsonOptions)
            ?? throw new InvalidOperationException("Config JSON illisible : désérialisation vide.");

        if (string.IsNullOrWhiteSpace(config.OriginalDesktopPath))
        {
            config.OriginalDesktopPath = currentDesktopPath;
            _logger.Warn($"originalDesktopPath absent : initialisé à {currentDesktopPath}");
        }

        if (string.IsNullOrWhiteSpace(config.RealmsRoot))
        {
            config.RealmsRoot = Path.Combine(config.OriginalDesktopPath, "DeskRealm");
            _logger.Warn($"realmsRoot absent : initialisé à {config.RealmsRoot}");
        }

        if (config.Version < 2)
        {
            if (config.IconLayoutAutoSaveEnabled)
            {
                config.IconLayoutAutoSaveEnabled = false;
                _logger.Warn("Migration config v2 : iconLayoutAutoSaveEnabled désactivé pour supprimer le polling Shell périodique.");
            }

            if (config.IconLayoutAutoSaveIntervalMs < 60000)
            {
                config.IconLayoutAutoSaveIntervalMs = 60000;
            }

            config.Version = 2;
        }

        if (config.Version < 3)
        {
            config.IconLayoutDisplayTopologyGuardEnabled = true;
            if (config.IconLayoutDisplayTopologySettleDelayMs < 1200)
            {
                config.IconLayoutDisplayTopologySettleDelayMs = 1200;
            }

            config.Version = 3;
            _logger.Warn("Migration config v3 : garde topologie écran/DPI activée pour éviter les sauvegardes contaminées multi-écran/résolution/scale.");
        }

        if (config.PollIntervalMs < 250)
        {
            throw new InvalidOperationException("pollIntervalMs est trop bas. Valeur minimale stricte : 250 ms.");
        }

        if (config.NextRealmNumber < 1)
        {
            throw new InvalidOperationException("nextRealmNumber invalide. Valeur minimale stricte : 1.");
        }

        if (config.RealmNameMaxLength is < 16 or > 120)
        {
            throw new InvalidOperationException("realmNameMaxLength invalide. Valeur stricte autorisée : 16 à 120 caractères.");
        }

        if (config.IconLayoutSettleDelayMs is < 0 or > 5000)
        {
            throw new InvalidOperationException("iconLayoutSettleDelayMs invalide. Valeur stricte autorisée : 0 à 5000 ms.");
        }

        if (config.IconLayoutWorkerTimeoutMs is < 1000 or > 60000)
        {
            throw new InvalidOperationException("iconLayoutWorkerTimeoutMs invalide. Valeur stricte autorisée : 1000 à 60000 ms.");
        }

        if (config.IconLayoutDisplayTopologySettleDelayMs is < 0 or > 10000)
        {
            throw new InvalidOperationException("iconLayoutDisplayTopologySettleDelayMs invalide. Valeur stricte autorisée : 0 à 10000 ms.");
        }

        if (config.IconLayoutAutoSaveIntervalMs is < 0 or > 300000)
        {
            throw new InvalidOperationException("iconLayoutAutoSaveIntervalMs invalide. Valeur stricte autorisée : 0 à 300000 ms.");
        }

        if (config.HotkeySwitchStepDelayMs is < 50 or > 1500)
        {
            throw new InvalidOperationException("hotkeySwitchStepDelayMs invalide. Valeur stricte autorisée : 50 à 1500 ms.");
        }

        if (config.HotkeySwitchSettleTimeoutMs is < 500 or > 15000)
        {
            throw new InvalidOperationException("hotkeySwitchSettleTimeoutMs invalide. Valeur stricte autorisée : 500 à 15000 ms.");
        }

        if (config.DesktopHotkeysEnabled)
        {
            ValidateDesktopHotkeys(config);
        }

        Save(config);
        return config;
    }


    private static void ValidateDesktopHotkeys(RealmConfig config)
    {
        if (config.DesktopHotkeys.Count == 0)
        {
            throw new InvalidOperationException("desktopHotkeysEnabled=true mais desktopHotkeys est vide.");
        }

        var seenCombos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in config.DesktopHotkeys)
        {
            if (!int.TryParse(pair.Key, out var desktopNumber) || desktopNumber < 1 || desktopNumber > 32)
            {
                throw new InvalidOperationException($"desktopHotkeys contient une clé de bureau invalide : '{pair.Key}'. Valeur attendue : 1 à 32.");
            }

            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                throw new InvalidOperationException($"desktopHotkeys[{pair.Key}] est vide.");
            }

            if (!seenCombos.Add(pair.Value.Trim()))
            {
                throw new InvalidOperationException($"Raccourci hotkey dupliqué : {pair.Value}.");
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
