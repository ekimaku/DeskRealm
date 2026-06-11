using System.Text.Json.Serialization;

namespace DeskRealm.App.Services;

internal sealed class DesktopIconLayout
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 3;

    [JsonPropertyName("virtualDesktopId")]
    public string VirtualDesktopId { get; set; } = string.Empty;

    [JsonPropertyName("realmName")]
    public string RealmName { get; set; } = string.Empty;

    [JsonPropertyName("savedAt")]
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyName("displayTopologyKey")]
    public string DisplayTopologyKey { get; set; } = string.Empty;

    [JsonPropertyName("displayTopologyFamilyKey")]
    public string DisplayTopologyFamilyKey { get; set; } = string.Empty;

    [JsonPropertyName("displayTopology")]
    public DisplayTopologySnapshot? DisplayTopology { get; set; }

    // Legacy/current shortcut retained for readability and v0.5.0-v0.5.2 compatibility.
    [JsonPropertyName("icons")]
    public List<DesktopIconPosition> Icons { get; set; } = [];

    [JsonPropertyName("variants")]
    public List<DesktopIconLayoutVariant> Variants { get; set; } = [];
}

internal sealed class DesktopIconLayoutVariant
{
    [JsonPropertyName("displayTopologyKey")]
    public string DisplayTopologyKey { get; set; } = string.Empty;

    [JsonPropertyName("displayTopologyFamilyKey")]
    public string DisplayTopologyFamilyKey { get; set; } = string.Empty;

    [JsonPropertyName("displayTopology")]
    public DisplayTopologySnapshot? DisplayTopology { get; set; }

    [JsonPropertyName("savedAt")]
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyName("icons")]
    public List<DesktopIconPosition> Icons { get; set; } = [];
}

internal sealed class DesktopIconPosition
{
    [JsonPropertyName("itemKey")]
    public string ItemKey { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("shellDisplayName")]
    public string ShellDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("shellParsingName")]
    public string ShellParsingName { get; set; } = string.Empty;

    [JsonPropertyName("identityKeys")]
    public List<string> IdentityKeys { get; set; } = [];

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("screenDeviceName")]
    public string ScreenDeviceName { get; set; } = string.Empty;

    [JsonPropertyName("screenRelativeX")]
    public int ScreenRelativeX { get; set; }

    [JsonPropertyName("screenRelativeY")]
    public int ScreenRelativeY { get; set; }

    [JsonPropertyName("screenRelativeXRatio")]
    public double ScreenRelativeXRatio { get; set; }

    [JsonPropertyName("screenRelativeYRatio")]
    public double ScreenRelativeYRatio { get; set; }
}

internal sealed class DisplayTopologySnapshot
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("familyKey")]
    public string FamilyKey { get; set; } = string.Empty;

    [JsonPropertyName("capturedAt")]
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyName("virtualBoundsX")]
    public int VirtualBoundsX { get; set; }

    [JsonPropertyName("virtualBoundsY")]
    public int VirtualBoundsY { get; set; }

    [JsonPropertyName("virtualBoundsWidth")]
    public int VirtualBoundsWidth { get; set; }

    [JsonPropertyName("virtualBoundsHeight")]
    public int VirtualBoundsHeight { get; set; }

    [JsonPropertyName("screens")]
    public List<DisplayScreenInfo> Screens { get; set; } = [];
}

internal sealed class DisplayScreenInfo
{
    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }

    [JsonPropertyName("boundsX")]
    public int BoundsX { get; set; }

    [JsonPropertyName("boundsY")]
    public int BoundsY { get; set; }

    [JsonPropertyName("boundsWidth")]
    public int BoundsWidth { get; set; }

    [JsonPropertyName("boundsHeight")]
    public int BoundsHeight { get; set; }

    [JsonPropertyName("workingX")]
    public int WorkingX { get; set; }

    [JsonPropertyName("workingY")]
    public int WorkingY { get; set; }

    [JsonPropertyName("workingWidth")]
    public int WorkingWidth { get; set; }

    [JsonPropertyName("workingHeight")]
    public int WorkingHeight { get; set; }

    [JsonPropertyName("effectiveDpiX")]
    public int EffectiveDpiX { get; set; }

    [JsonPropertyName("effectiveDpiY")]
    public int EffectiveDpiY { get; set; }

    [JsonPropertyName("scalePercent")]
    public int ScalePercent { get; set; }

    [JsonPropertyName("orientation")]
    public string Orientation { get; set; } = string.Empty;
}
