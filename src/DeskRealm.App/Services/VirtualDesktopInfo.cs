namespace DeskRealm.App.Services;

/// <summary>
/// Snapshot of one Windows virtual desktop. <c>NameIsFallback</c> is true only when
/// Explorer did not currently expose a stored name and DeskRealm had no earlier confirmed
/// label for that GUID in this process.
/// </summary>
internal sealed record VirtualDesktopInfo(Guid Id, string Name, int Number, bool NameIsFallback = false);
