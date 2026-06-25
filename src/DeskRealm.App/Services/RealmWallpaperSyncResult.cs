namespace DeskRealm.App.Services;

/// <summary>
/// Presentation-safe result of reconciling Windows' per-desktop Registry value with
/// DeskRealm-owned wallpaper metadata. A missing or unreadable external file is never
/// silently replaced; the UI receives an explicit preview state instead.
/// </summary>
internal sealed record RealmWallpaperSyncResult(
    RealmWallpaper? Wallpaper,
    string PreviewPath,
    string DisplayName,
    string Status,
    bool HasPreview,
    bool ImportedFromWindows)
{
    public static RealmWallpaperSyncResult NoWallpaper { get; } = new(
        null,
        string.Empty,
        "No wallpaper assigned",
        "No Windows wallpaper assignment is stored for this realm.",
        false,
        false);
}
