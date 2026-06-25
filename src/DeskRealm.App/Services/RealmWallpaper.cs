using System.Text.Json.Serialization;

namespace DeskRealm.App.Services;

/// <summary>
/// DeskRealm-owned metadata for a wallpaper selected by the user. The actual Windows
/// association remains stored under the current user's virtual-desktop registry data.
/// The managed copy makes the assignment stable when the original selected file moves.
/// </summary>
internal sealed class RealmWallpaper
{
    [JsonPropertyName("managedPath")]
    public string ManagedPath { get; set; } = string.Empty;

    [JsonPropertyName("sourceFileName")]
    public string SourceFileName { get; set; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.MinValue;
}
