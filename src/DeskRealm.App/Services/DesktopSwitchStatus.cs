namespace DeskRealm.App.Services;

internal sealed record DesktopSwitchStatus(
    bool Enabled,
    string CurrentDesktopName,
    string CurrentDesktopGuid,
    string CurrentRealmPath,
    string KnownFolderDesktopPath,
    DateTimeOffset LastSwitchAt,
    string LastMessage,
    string Assignments);
