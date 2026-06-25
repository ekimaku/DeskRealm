using System.Text.Json.Serialization;

namespace DeskRealm.App.Services;

/// <summary>
/// User-owned metadata attached to a Windows virtual-desktop GUID. Windows owns the
/// desktop itself; DeskRealm owns only this presentation and startup metadata.
/// </summary>
internal sealed class RealmProfile
{
    [JsonPropertyName("isFavorite")]
    public bool IsFavorite { get; set; }

    [JsonPropertyName("activateOnDeskRealmStartup")]
    public bool ActivateOnDeskRealmStartup { get; set; }
}
