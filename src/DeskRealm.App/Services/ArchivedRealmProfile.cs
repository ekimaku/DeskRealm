using System.Text.Json.Serialization;

namespace DeskRealm.App.Services;

/// <summary>
/// A deliberately retained record when a Windows virtual desktop is closed. It enables
/// a later desktop with the same realm name to explicitly reuse an old icon layout.
/// Nothing is restored implicitly.
/// </summary>
internal sealed class ArchivedRealmProfile
{
    [JsonPropertyName("sourceDesktopId")]
    public string SourceDesktopId { get; set; } = string.Empty;

    [JsonPropertyName("desktopName")]
    public string DesktopName { get; set; } = string.Empty;

    [JsonPropertyName("realmAssignment")]
    public string RealmAssignment { get; set; } = string.Empty;

    [JsonPropertyName("wallpaper")]
    public RealmWallpaper? Wallpaper { get; set; }

    [JsonPropertyName("archivedAt")]
    public DateTimeOffset ArchivedAt { get; set; } = DateTimeOffset.MinValue;
}
