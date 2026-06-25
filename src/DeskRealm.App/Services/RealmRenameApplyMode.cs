namespace DeskRealm.App.Services;

/// <summary>
/// Controls when a Registry-backed Windows virtual-desktop name becomes visible in
/// Task View. Windows persists the name in Explorer metadata, but the running shell
/// reloads it only after Explorer starts again.
/// </summary>
internal enum RealmRenameApplyMode
{
    /// <summary>Ask in Realm Studio every time a virtual desktop label changes.</summary>
    Ask = 0,

    /// <summary>Restart the current-session Explorer shell immediately after the Registry write.</summary>
    RestartExplorer = 1,

    /// <summary>Keep the Registry write and let the next Explorer start/reboot apply it.</summary>
    NextReboot = 2
}

internal sealed record RealmRenameApplyChoice(
    RealmRenameApplyMode Mode,
    bool RememberChoice);
