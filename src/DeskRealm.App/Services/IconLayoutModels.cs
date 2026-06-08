using System.Text.Json.Serialization;

namespace DeskRealm.App.Services;

internal sealed class DesktopIconLayout
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 2;

    [JsonPropertyName("virtualDesktopId")]
    public string VirtualDesktopId { get; set; } = string.Empty;

    [JsonPropertyName("realmName")]
    public string RealmName { get; set; } = string.Empty;

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

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}
