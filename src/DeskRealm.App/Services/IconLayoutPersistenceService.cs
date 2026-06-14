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

    public void SaveLockedMergeNewIcons(Guid virtualDesktopId, string realmName)
    {
        SaveLockedMergeNewIconsInternal(virtualDesktopId, realmName);
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
            ?? throw new InvalidOperationException($"Unreadable icon layout: empty deserialization ({path}).");

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

    public bool DeleteVariant(Guid virtualDesktopId, string displayTopologyKey)
    {
        if (string.IsNullOrWhiteSpace(displayTopologyKey) ||
            string.Equals(displayTopologyKey, "pending-baseline", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cannot delete an icon layout variant without a persisted display topology key.");
        }

        var path = GetLayoutPath(virtualDesktopId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Icon layout file not found: {path}", path);
        }

        var raw = File.ReadAllText(path);
        var layout = JsonSerializer.Deserialize<DesktopIconLayout>(raw, _jsonOptions)
            ?? throw new InvalidOperationException($"Unreadable icon layout: empty deserialization ({path}).");

        ValidateLayoutOwner(layout, virtualDesktopId, path);

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

        var before = layout.Variants.Count;
        layout.Variants = layout.Variants
            .Where(v => !string.Equals(v.DisplayTopologyKey, displayTopologyKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.SavedAt)
            .ToList();

        if (layout.Variants.Count == before)
        {
            throw new InvalidOperationException($"Icon layout variant not found for topology key: {displayTopologyKey}");
        }

        if (layout.Variants.Count == 0)
        {
            File.Delete(path);
            _logger.Info($"Icon layout variant deleted and empty layout file removed: {virtualDesktopId:B} (topology={displayTopologyKey}, path={path})");
            return true;
        }

        var latest = layout.Variants.OrderByDescending(v => v.SavedAt).First();
        layout.Version = 3;
        layout.SavedAt = latest.SavedAt;
        layout.DisplayTopologyKey = latest.DisplayTopologyKey;
        layout.DisplayTopologyFamilyKey = latest.DisplayTopologyFamilyKey;
        layout.DisplayTopology = latest.DisplayTopology;
        layout.Icons = latest.Icons;

        File.WriteAllText(path, JsonSerializer.Serialize(layout, _jsonOptions));
        _logger.Info($"Icon layout variant deleted: {virtualDesktopId:B} (topology={displayTopologyKey}, remaining={layout.Variants.Count}, path={path})");
        return true;
    }

    public string GetLayoutPath(Guid virtualDesktopId)
    {
        return GetLayoutPathForDesktop(virtualDesktopId);
    }

    public static string GetLayoutPathForDesktop(Guid virtualDesktopId)
    {
        var safeGuid = virtualDesktopId.ToString("N");
        return Path.Combine(LayoutRoot, safeGuid + ".json");
    }


    public static IReadOnlyList<IconLayoutVariantFileSnapshot> ReadLayoutVariantsForDesktop(Guid virtualDesktopId)
    {
        var path = GetLayoutPathForDesktop(virtualDesktopId);
        if (!File.Exists(path))
        {
            return [];
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var raw = File.ReadAllText(path);
        var layout = JsonSerializer.Deserialize<DesktopIconLayout>(raw, options)
            ?? throw new InvalidOperationException($"Unreadable icon layout: empty deserialization ({path}).");

        ValidateLayoutOwner(layout, virtualDesktopId, path);

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

        return layout.Variants
            .Where(v => !string.IsNullOrWhiteSpace(v.DisplayTopologyKey))
            .OrderByDescending(v => v.SavedAt)
            .Select((variant, index) => new IconLayoutVariantFileSnapshot(
                variant.DisplayTopologyKey,
                string.IsNullOrWhiteSpace(variant.DisplayTopologyFamilyKey) ? "unknown-family" : variant.DisplayTopologyFamilyKey,
                variant.SavedAt,
                variant.Icons.Count,
                BuildVariantSummary(variant, index + 1),
                BuildVariantDisplays(variant.DisplayTopology)))
            .ToList();
    }

    private static string BuildVariantSummary(DesktopIconLayoutVariant variant, int index)
    {
        if (variant.DisplayTopology is null)
        {
            return $"Variant {index} · {ShortTopologyKey(variant.DisplayTopologyKey)}";
        }

        return $"Variant {index}";
    }

    private static IReadOnlyList<IconLayoutDisplayFileSnapshot> BuildVariantDisplays(DisplayTopologySnapshot? topology)
    {
        if (topology is null)
        {
            return [];
        }

        return topology.Screens
            .Select((screen, index) => new IconLayoutDisplayFileSnapshot(
                string.IsNullOrWhiteSpace(screen.DeviceName) ? $"Display {index + 1}" : screen.DeviceName,
                screen.Primary,
                screen.WorkingWidth,
                screen.WorkingHeight,
                screen.ScalePercent,
                string.IsNullOrWhiteSpace(screen.Orientation) ? "unknown" : screen.Orientation))
            .ToList();
    }

    private static string ShortTopologyKey(string topologyKey)
    {
        if (string.IsNullOrWhiteSpace(topologyKey))
        {
            return "unknown topology";
        }

        return topologyKey.Length <= 18 ? topologyKey : topologyKey[..18] + "…";
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

    private void SaveLockedMergeNewIconsInternal(Guid virtualDesktopId, string realmName)
    {
        Directory.CreateDirectory(LayoutRoot);

        var topology = DisplayTopologyService.Capture();
        var capturedIcons = EnrichIconsWithDisplayTopology(_desktopIcons.CapturePositions(), topology).ToList();
        var path = GetLayoutPath(virtualDesktopId);
        var now = DateTimeOffset.Now;

        var layout = LoadExistingLayout(path, virtualDesktopId, realmName);
        ValidateLayoutOwner(layout, virtualDesktopId, path);

        var existingVariant = layout.Variants.FirstOrDefault(v =>
            string.Equals(v.DisplayTopologyKey, topology.Key, StringComparison.OrdinalIgnoreCase));

        if (existingVariant is null || existingVariant.Icons.Count == 0)
        {
            SaveInternal(virtualDesktopId, realmName, saveOnlyIfChanged: false);
            _logger.Info($"Icon layout locked baseline saved: {realmName} {virtualDesktopId:B} -> {capturedIcons.Count} icons (topology={topology.Key}, path={path})");
            return;
        }

        var existingIdentityKeys = BuildIconIdentitySet(existingVariant.Icons);
        var newIcons = capturedIcons
            .Where(icon => !IconMatchesExisting(icon, existingIdentityKeys))
            .ToList();

        if (newIcons.Count == 0)
        {
            _logger.Info($"Icon layout locked autosave skipped: no new icons detected for {realmName} {virtualDesktopId:B} under topology {topology.Key}.");
            return;
        }

        var mergedIcons = existingVariant.Icons
            .Concat(newIcons)
            .OrderBy(i => i.Y)
            .ThenBy(i => i.X)
            .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var replacement = new DesktopIconLayoutVariant
        {
            DisplayTopologyKey = topology.Key,
            DisplayTopologyFamilyKey = topology.FamilyKey,
            DisplayTopology = topology,
            SavedAt = now,
            Icons = mergedIcons
        };

        layout.Version = 3;
        layout.VirtualDesktopId = virtualDesktopId.ToString("B");
        layout.RealmName = realmName;
        layout.SavedAt = now;
        layout.DisplayTopologyKey = topology.Key;
        layout.DisplayTopologyFamilyKey = topology.FamilyKey;
        layout.DisplayTopology = topology;
        layout.Icons = mergedIcons;
        layout.Variants = layout.Variants
            .Where(v => !string.Equals(v.DisplayTopologyKey, topology.Key, StringComparison.OrdinalIgnoreCase))
            .Append(replacement)
            .OrderByDescending(v => v.SavedAt)
            .Take(24)
            .ToList();

        var raw = JsonSerializer.Serialize(layout, _jsonOptions);
        File.WriteAllText(path, raw);

        _logger.Info(
            $"Icon layout locked autosave merged new icons: {realmName} {virtualDesktopId:B} -> " +
            $"new={newIcons.Count}, total={mergedIcons.Count}, topology={topology.Key}, variants={layout.Variants.Count}, path={path}");
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
            ?? throw new InvalidOperationException($"Unreadable icon layout: empty deserialization ({path}).");

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
                $"Invalid icon layout: file {path} declares {layout.VirtualDesktopId}, expected {virtualDesktopId:B}.");
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

    private static HashSet<string> BuildIconIdentitySet(IEnumerable<DesktopIconPosition> icons)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var icon in icons)
        {
            foreach (var key in EnumerateIconIdentityKeys(icon))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private static bool IconMatchesExisting(DesktopIconPosition icon, HashSet<string> existingIdentityKeys)
    {
        return EnumerateIconIdentityKeys(icon).Any(existingIdentityKeys.Contains);
    }

    private static IEnumerable<string> EnumerateIconIdentityKeys(DesktopIconPosition icon)
    {
        if (!string.IsNullOrWhiteSpace(icon.ItemKey))
        {
            yield return "item:" + icon.ItemKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(icon.ShellParsingName))
        {
            yield return "parsing:" + icon.ShellParsingName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(icon.ShellDisplayName))
        {
            yield return "shell-display:" + icon.ShellDisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(icon.DisplayName))
        {
            yield return "display:" + icon.DisplayName.Trim();
        }

        foreach (var identity in icon.IdentityKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            yield return "identity:" + identity.Trim();
        }
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

internal sealed record IconLayoutVariantFileSnapshot(
    string DisplayTopologyKey,
    string DisplayTopologyFamilyKey,
    DateTimeOffset SavedAt,
    int IconCount,
    string Summary,
    IReadOnlyList<IconLayoutDisplayFileSnapshot> Displays);

internal sealed record IconLayoutDisplayFileSnapshot(
    string DeviceName,
    bool Primary,
    int WorkingWidth,
    int WorkingHeight,
    int ScalePercent,
    string Orientation);
