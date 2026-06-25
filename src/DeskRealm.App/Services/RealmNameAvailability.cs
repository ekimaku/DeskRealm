namespace DeskRealm.App.Services;

/// <summary>
/// Immutable name-resolution snapshot used by both Realm Studio validation and the
/// serialized rename mutation. A live desktop always wins over an archived profile:
/// DeskRealm never merges two active Windows desktops by name.
/// </summary>
internal sealed record RealmNameAvailability(
    Guid DesktopId,
    string RequestedDisplayName,
    string NormalizedRealmFolderName,
    bool IsUnchanged,
    VirtualDesktopInfo? ActiveConflict,
    ArchivedRealmProfile? ArchivedProfile)
{
    public bool HasActiveConflict => ActiveConflict is not null;
    public bool HasArchivedRealm => ArchivedProfile is not null;

    public static RealmNameAvailability Unchanged(Guid desktopId, string requestedDisplayName, string normalizedRealmFolderName)
        => new(desktopId, requestedDisplayName, normalizedRealmFolderName, true, null, null);

    public static RealmNameAvailability Available(Guid desktopId, string requestedDisplayName, string normalizedRealmFolderName, ArchivedRealmProfile? archivedProfile)
        => new(desktopId, requestedDisplayName, normalizedRealmFolderName, false, null, archivedProfile);

    public static RealmNameAvailability LiveConflict(Guid desktopId, string requestedDisplayName, string normalizedRealmFolderName, VirtualDesktopInfo activeConflict)
        => new(desktopId, requestedDisplayName, normalizedRealmFolderName, false, activeConflict, null);
}
