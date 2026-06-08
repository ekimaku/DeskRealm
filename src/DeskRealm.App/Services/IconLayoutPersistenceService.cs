using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeskRealm.App.Services;

internal sealed class IconLayoutPersistenceService
{
    private readonly DesktopIconShellService _desktopIcons;
    private readonly FileLogger _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public IconLayoutPersistenceService(DesktopIconShellService desktopIcons, FileLogger logger)
    {
        _desktopIcons = desktopIcons;
        _logger = logger;
        Directory.CreateDirectory(LayoutRoot);
    }

    public static string LayoutRoot => Path.Combine(AppPaths.AppDataRoot, "icon-layouts");

    public void Save(Guid virtualDesktopId, string realmName)
    {
        SaveInternal(virtualDesktopId, realmName, saveOnlyIfChanged: false);
    }

    public void SaveIfChanged(Guid virtualDesktopId, string realmName)
    {
        SaveInternal(virtualDesktopId, realmName, saveOnlyIfChanged: true);
    }

    public void Restore(Guid virtualDesktopId, string realmName)
    {
        var path = GetLayoutPath(virtualDesktopId);
        if (!File.Exists(path))
        {
            _logger.Info($"Icon layout restore skipped: no saved layout for {realmName} {virtualDesktopId:B}.");
            return;
        }

        var raw = File.ReadAllText(path);
        var layout = JsonSerializer.Deserialize<DesktopIconLayout>(raw, _jsonOptions)
            ?? throw new InvalidOperationException($"Layout icônes illisible : désérialisation vide ({path}).");

        if (!string.Equals(layout.VirtualDesktopId, virtualDesktopId.ToString("B"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Layout icônes invalide : le fichier {path} déclare {layout.VirtualDesktopId}, attendu {virtualDesktopId:B}.");
        }

        var restored = _desktopIcons.RestorePositions(layout.Icons);
        _logger.Info($"Icon layout restored: {realmName} {virtualDesktopId:B} -> {restored}/{layout.Icons.Count} icons ({path})");
    }

    public string GetLayoutPath(Guid virtualDesktopId)
    {
        var safeGuid = virtualDesktopId.ToString("N");
        return Path.Combine(LayoutRoot, safeGuid + ".json");
    }

    private void SaveInternal(Guid virtualDesktopId, string realmName, bool saveOnlyIfChanged)
    {
        Directory.CreateDirectory(LayoutRoot);

        var icons = _desktopIcons.CapturePositions().ToList();
        var path = GetLayoutPath(virtualDesktopId);

        if (saveOnlyIfChanged && File.Exists(path))
        {
            var existingRaw = File.ReadAllText(path);
            var existing = JsonSerializer.Deserialize<DesktopIconLayout>(existingRaw, _jsonOptions);
            if (existing is not null && LayoutFingerprint(existing.Icons) == LayoutFingerprint(icons))
            {
                _logger.Info($"Icon layout autosave skipped: no layout change detected for {realmName} {virtualDesktopId:B}.");
                return;
            }
        }

        var layout = new DesktopIconLayout
        {
            VirtualDesktopId = virtualDesktopId.ToString("B"),
            RealmName = realmName,
            SavedAt = DateTimeOffset.Now,
            Icons = icons
        };

        var raw = JsonSerializer.Serialize(layout, _jsonOptions);
        File.WriteAllText(path, raw);

        var mode = saveOnlyIfChanged ? "autosaved" : "saved";
        _logger.Info($"Icon layout {mode}: {realmName} {virtualDesktopId:B} -> {icons.Count} icons ({path})");
    }

    private static string LayoutFingerprint(IEnumerable<DesktopIconPosition> icons)
    {
        var builder = new StringBuilder();
        foreach (var icon in icons
                     .Where(i => !string.IsNullOrWhiteSpace(i.ItemKey))
                     .OrderBy(i => i.ItemKey, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(icon.ItemKey).Append('|').Append(icon.X).Append('|').Append(icon.Y).Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
