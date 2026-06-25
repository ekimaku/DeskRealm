namespace DeskRealm.App.Services;

internal sealed record DesktopSwitchStatus(
    string CurrentDesktopName,
    string CurrentDesktopGuid,
    string CurrentRealmPath,
    string KnownFolderDesktopPath,
    DateTimeOffset LastSwitchAt,
    string LastMessage,
    string Assignments);
