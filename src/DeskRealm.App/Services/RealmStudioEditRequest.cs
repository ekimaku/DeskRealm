namespace DeskRealm.App.Services;

internal enum RealmDuplicateResolution
{
    Ask = 0,
    ReuseArchivedLayout = 1,
    OverwriteArchivedLayout = 2
}

/// <summary>
/// Full edit payload from the global Realm Studio modal. The UI stays declarative;
/// all validation, persistence and Windows mutations occur through DesktopSwitchService.
/// </summary>
internal sealed record RealmStudioEditRequest(
    string DisplayName,
    bool IsDefaultRealm,
    string? Hotkey,
    string? WallpaperSourcePath,
    bool ClearWallpaper,
    bool RealmLocked,
    bool LayoutLocked,
    IReadOnlyDictionary<string, bool> VariantLocks,
    IReadOnlyList<string> DeleteVariants,
    RealmDuplicateResolution DuplicateResolution)
{
    // The editor itself does not know whether a name changed. MainWindow adds this
    // explicit Shell-application choice only after the user confirms the rename.
    public RealmRenameApplyMode RenameApplyMode { get; init; } = RealmRenameApplyMode.NextReboot;

    public bool RememberRenameApplyMode { get; init; }
}
