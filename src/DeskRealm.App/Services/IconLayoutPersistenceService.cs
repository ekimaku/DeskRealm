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

        var currentTopology = DisplayTopologyService.Capture();
        var raw = File.ReadAllText(path);
        var layout = JsonSerializer.Deserialize<DesktopIconLayout>(raw, _jsonOptions)
            ?? throw new InvalidOperationException($"Layout icônes illisible : désérialisation vide ({path}).");

        ValidateLayoutOwner(layout, virtualDesktopId, path);

        var selected = SelectVariant(layout, currentTopology);
        if (selected.Icons.Count == 0)
        {
            _logger.Info($"Icon layout restore skipped: selected layout is empty for {realmName} {virtualDesktopId:B}.");
            return;
        }

        var iconsToRestore = string.Equals(selected.MatchKind, "exact-topology", StringComparison.OrdinalIgnoreCase)
            ? selected.Icons
            : AdaptIconsToCurrentTopology(selected.Icons, selected.SourceTopology, currentTopology);

        var restored = _desktopIcons.RestorePositions(iconsToRestore);
        _logger.Info(
            $"Icon layout restored: {realmName} {virtualDesktopId:B} -> {restored}/{iconsToRestore.Count} icons " +
            $"(variant={selected.MatchKind}, topology={currentTopology.Key}, path={path})");
    }

    public string GetLayoutPath(Guid virtualDesktopId)
    {
        var safeGuid = virtualDesktopId.ToString("N");
        return Path.Combine(LayoutRoot, safeGuid + ".json");
    }

    private void SaveInternal(Guid virtualDesktopId, string realmName, bool saveOnlyIfChanged)
    {
        Directory.CreateDirectory(LayoutRoot);

        var topology = DisplayTopologyService.Capture();
        var icons = EnrichIconsWithDisplayTopology(_desktopIcons.CapturePositions(), topology).ToList();
        var path = GetLayoutPath(virtualDesktopId);
        var now = DateTimeOffset.Now;

        var layout = LoadExistingLayout(path, virtualDesktopId, realmName);
        ValidateLayoutOwner(layout, virtualDesktopId, path);

        var existingVariant = layout.Variants.FirstOrDefault(v =>
            string.Equals(v.DisplayTopologyKey, topology.Key, StringComparison.OrdinalIgnoreCase));

        if (saveOnlyIfChanged && existingVariant is not null &&
            LayoutFingerprint(existingVariant.Icons) == LayoutFingerprint(icons))
        {
            _logger.Info($"Icon layout autosave skipped: no layout change detected for {realmName} {virtualDesktopId:B} under topology {topology.Key}.");
            return;
        }

        var replacement = new DesktopIconLayoutVariant
        {
            DisplayTopologyKey = topology.Key,
            DisplayTopologyFamilyKey = topology.FamilyKey,
            DisplayTopology = topology,
            SavedAt = now,
            Icons = icons
        };

        layout.Version = 3;
        layout.VirtualDesktopId = virtualDesktopId.ToString("B");
        layout.RealmName = realmName;
        layout.SavedAt = now;
        layout.DisplayTopologyKey = topology.Key;
        layout.DisplayTopologyFamilyKey = topology.FamilyKey;
        layout.DisplayTopology = topology;
        layout.Icons = icons;
        layout.Variants = layout.Variants
            .Where(v => !string.Equals(v.DisplayTopologyKey, topology.Key, StringComparison.OrdinalIgnoreCase))
            .Append(replacement)
            .OrderByDescending(v => v.SavedAt)
            .Take(24)
            .ToList();

        var raw = JsonSerializer.Serialize(layout, _jsonOptions);
        File.WriteAllText(path, raw);

        var mode = saveOnlyIfChanged ? "autosaved" : "saved";
        _logger.Info($"Icon layout {mode}: {realmName} {virtualDesktopId:B} -> {icons.Count} icons (topology={topology.Key}, variants={layout.Variants.Count}, path={path})");
    }

    private DesktopIconLayout LoadExistingLayout(string path, Guid virtualDesktopId, string realmName)
    {
        if (!File.Exists(path))
        {
            return new DesktopIconLayout
            {
                Version = 3,
                VirtualDesktopId = virtualDesktopId.ToString("B"),
                RealmName = realmName
            };
        }

        var raw = File.ReadAllText(path);
        var layout = JsonSerializer.Deserialize<DesktopIconLayout>(raw, _jsonOptions)
            ?? throw new InvalidOperationException($"Layout icônes illisible : désérialisation vide ({path}).");

        if (layout.Variants.Count == 0 && layout.Icons.Count > 0)
        {
            layout.Variants.Add(new DesktopIconLayoutVariant
            {
                DisplayTopologyKey = string.IsNullOrWhiteSpace(layout.DisplayTopologyKey) ? "legacy:no-topology" : layout.DisplayTopologyKey,
                DisplayTopologyFamilyKey = string.IsNullOrWhiteSpace(layout.DisplayTopologyFamilyKey) ? "legacy:no-family" : layout.DisplayTopologyFamilyKey,
                DisplayTopology = layout.DisplayTopology,
                SavedAt = layout.SavedAt,
                Icons = layout.Icons
            });
        }

        return layout;
    }

    private static void ValidateLayoutOwner(DesktopIconLayout layout, Guid virtualDesktopId, string path)
    {
        if (!string.Equals(layout.VirtualDesktopId, virtualDesktopId.ToString("B"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Layout icônes invalide : le fichier {path} déclare {layout.VirtualDesktopId}, attendu {virtualDesktopId:B}.");
        }
    }

    private static SelectedIconVariant SelectVariant(DesktopIconLayout layout, DisplayTopologySnapshot currentTopology)
    {
        var exact = layout.Variants
            .Where(v => string.Equals(v.DisplayTopologyKey, currentTopology.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.SavedAt)
            .FirstOrDefault();
        if (exact is not null)
        {
            return new SelectedIconVariant("exact-topology", exact.Icons, exact.DisplayTopology);
        }

        var sameFamily = layout.Variants
            .Where(v => !string.IsNullOrWhiteSpace(v.DisplayTopologyFamilyKey) &&
                        string.Equals(v.DisplayTopologyFamilyKey, currentTopology.FamilyKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.SavedAt)
            .FirstOrDefault();
        if (sameFamily is not null)
        {
            return new SelectedIconVariant("same-monitor-family-best-effort", sameFamily.Icons, sameFamily.DisplayTopology);
        }

        var latest = layout.Variants
            .OrderByDescending(v => v.SavedAt)
            .FirstOrDefault();
        if (latest is not null)
        {
            return new SelectedIconVariant("latest-topology-best-effort", latest.Icons, latest.DisplayTopology);
        }

        return new SelectedIconVariant("legacy-icons", layout.Icons, layout.DisplayTopology);
    }

    private static IReadOnlyList<DesktopIconPosition> EnrichIconsWithDisplayTopology(IEnumerable<DesktopIconPosition> icons, DisplayTopologySnapshot topology)
    {
        return icons.Select(icon => EnrichIcon(icon, topology)).ToList();
    }

    private static DesktopIconPosition EnrichIcon(DesktopIconPosition icon, DisplayTopologySnapshot topology)
    {
        var screen = FindScreenForPoint(topology, icon.X, icon.Y);
        if (screen is null)
        {
            return icon;
        }

        var relativeX = icon.X - screen.BoundsX;
        var relativeY = icon.Y - screen.BoundsY;
        return new DesktopIconPosition
        {
            ItemKey = icon.ItemKey,
            DisplayName = icon.DisplayName,
            ShellDisplayName = icon.ShellDisplayName,
            ShellParsingName = icon.ShellParsingName,
            IdentityKeys = icon.IdentityKeys.ToList(),
            X = icon.X,
            Y = icon.Y,
            ScreenDeviceName = screen.DeviceName,
            ScreenRelativeX = relativeX,
            ScreenRelativeY = relativeY,
            ScreenRelativeXRatio = screen.BoundsWidth <= 0 ? 0 : relativeX / (double)screen.BoundsWidth,
            ScreenRelativeYRatio = screen.BoundsHeight <= 0 ? 0 : relativeY / (double)screen.BoundsHeight
        };
    }

    private static List<DesktopIconPosition> AdaptIconsToCurrentTopology(
        IReadOnlyList<DesktopIconPosition> icons,
        DisplayTopologySnapshot? sourceTopology,
        DisplayTopologySnapshot currentTopology)
    {
        if (currentTopology.Screens.Count == 0)
        {
            return icons.ToList();
        }

        return icons.Select(icon => AdaptIconToCurrentTopology(icon, currentTopology)).ToList();
    }

    private static DesktopIconPosition AdaptIconToCurrentTopology(DesktopIconPosition icon, DisplayTopologySnapshot currentTopology)
    {
        var targetScreen = currentTopology.Screens.FirstOrDefault(s =>
                !string.IsNullOrWhiteSpace(icon.ScreenDeviceName) &&
                string.Equals(s.DeviceName, icon.ScreenDeviceName, StringComparison.OrdinalIgnoreCase))
            ?? currentTopology.Screens.FirstOrDefault(s => s.Primary)
            ?? currentTopology.Screens.First();

        var x = icon.X;
        var y = icon.Y;
        if (icon.ScreenRelativeXRatio > 0 || icon.ScreenRelativeYRatio > 0)
        {
            x = targetScreen.BoundsX + (int)Math.Round(icon.ScreenRelativeXRatio * targetScreen.BoundsWidth);
            y = targetScreen.BoundsY + (int)Math.Round(icon.ScreenRelativeYRatio * targetScreen.BoundsHeight);
        }
        else if (!string.IsNullOrWhiteSpace(icon.ScreenDeviceName))
        {
            x = targetScreen.BoundsX + icon.ScreenRelativeX;
            y = targetScreen.BoundsY + icon.ScreenRelativeY;
        }

        x = Math.Clamp(x, targetScreen.BoundsX, targetScreen.BoundsX + Math.Max(0, targetScreen.BoundsWidth - 1));
        y = Math.Clamp(y, targetScreen.BoundsY, targetScreen.BoundsY + Math.Max(0, targetScreen.BoundsHeight - 1));

        return new DesktopIconPosition
        {
            ItemKey = icon.ItemKey,
            DisplayName = icon.DisplayName,
            ShellDisplayName = icon.ShellDisplayName,
            ShellParsingName = icon.ShellParsingName,
            IdentityKeys = icon.IdentityKeys.ToList(),
            X = x,
            Y = y,
            ScreenDeviceName = targetScreen.DeviceName,
            ScreenRelativeX = x - targetScreen.BoundsX,
            ScreenRelativeY = y - targetScreen.BoundsY,
            ScreenRelativeXRatio = targetScreen.BoundsWidth <= 0 ? 0 : (x - targetScreen.BoundsX) / (double)targetScreen.BoundsWidth,
            ScreenRelativeYRatio = targetScreen.BoundsHeight <= 0 ? 0 : (y - targetScreen.BoundsY) / (double)targetScreen.BoundsHeight
        };
    }

    private static DisplayScreenInfo? FindScreenForPoint(DisplayTopologySnapshot topology, int x, int y)
    {
        var containing = topology.Screens.FirstOrDefault(screen =>
            x >= screen.BoundsX && x < screen.BoundsX + screen.BoundsWidth &&
            y >= screen.BoundsY && y < screen.BoundsY + screen.BoundsHeight);
        if (containing is not null)
        {
            return containing;
        }

        return topology.Screens
            .OrderBy(screen => DistanceSquaredToScreen(screen, x, y))
            .FirstOrDefault();
    }

    private static long DistanceSquaredToScreen(DisplayScreenInfo screen, int x, int y)
    {
        var centerX = screen.BoundsX + screen.BoundsWidth / 2;
        var centerY = screen.BoundsY + screen.BoundsHeight / 2;
        var dx = centerX - x;
        var dy = centerY - y;
        return (long)dx * dx + (long)dy * dy;
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

    private readonly record struct SelectedIconVariant(string MatchKind, List<DesktopIconPosition> Icons, DisplayTopologySnapshot? SourceTopology);
}
