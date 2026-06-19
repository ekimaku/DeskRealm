using System.Text.Json.Serialization;

namespace DeskRealm.App.Services;

internal sealed class RealmConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 11;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("restoreDesktopOnExit")]
    public bool RestoreDesktopOnExit { get; set; } = true;

    [JsonPropertyName("rejectOneDriveDesktop")]
    public bool RejectOneDriveDesktop { get; set; } = true;

    [JsonPropertyName("syncRealmNamesWithVirtualDesktopNames")]
    public bool SyncRealmNamesWithVirtualDesktopNames { get; set; } = true;


    [JsonPropertyName("initialDesktopImportPromptEnabled")]
    public bool InitialDesktopImportPromptEnabled { get; set; } = true;

    [JsonPropertyName("initialDesktopImportPromptCompleted")]
    public bool InitialDesktopImportPromptCompleted { get; set; } = false;

    [JsonPropertyName("initialDesktopImportMoveFiles")]
    public bool InitialDesktopImportMoveFiles { get; set; } = false;

    [JsonPropertyName("initialDesktopImportSaveLayout")]
    public bool InitialDesktopImportSaveLayout { get; set; } = true;

    [JsonPropertyName("realmNameMaxLength")]
    public int RealmNameMaxLength { get; set; } = 80;

    [JsonPropertyName("iconLayoutPersistenceEnabled")]
    public bool IconLayoutPersistenceEnabled { get; set; } = true;

    [JsonPropertyName("iconLayoutWorkerTimeoutMs")]
    public int IconLayoutWorkerTimeoutMs { get; set; } = 8000;

    [JsonPropertyName("iconLayoutDisplayTopologyGuardEnabled")]
    public bool IconLayoutDisplayTopologyGuardEnabled { get; set; } = true;

    [JsonPropertyName("shellViewReadyTimeoutMs")]
    public int ShellViewReadyTimeoutMs { get; set; } = 2500;

    [JsonPropertyName("iconLayoutRestoreVerificationTimeoutMs")]
    public int IconLayoutRestoreVerificationTimeoutMs { get; set; } = 1400;

    [JsonPropertyName("desktopHotkeysEnabled")]
    public bool DesktopHotkeysEnabled { get; set; } = true;

    [JsonPropertyName("desktopHotkeys")]
    public Dictionary<string, string> DesktopHotkeys { get; set; } = CreateDefaultDesktopHotkeys();

    [JsonPropertyName("lockedIconLayouts")]
    public Dictionary<string, bool> LockedIconLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("lockedRealms")]
    public Dictionary<string, bool> LockedRealms { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("lockedIconLayoutVariants")]
    public Dictionary<string, bool> LockedIconLayoutVariants { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> CreateDefaultDesktopHotkeys()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["1"] = "Win+Shift+X",
            ["2"] = "Win+Shift+C",
            ["3"] = "Win+Shift+B",
            ["4"] = "Win+Shift+N"
        };
    }

    [JsonPropertyName("hotkeyModifierReleaseTimeoutMs")]
    public int HotkeyModifierReleaseTimeoutMs { get; set; } = 1200;

    [JsonPropertyName("desktopStepConfirmationTimeoutMs")]
    public int DesktopStepConfirmationTimeoutMs { get; set; } = 1800;

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; } = false;

    [JsonPropertyName("originalDesktopPath")]
    public string? OriginalDesktopPath { get; set; }

    [JsonPropertyName("realmsRoot")]
    public string? RealmsRoot { get; set; }

    [JsonPropertyName("nextRealmNumber")]
    public int NextRealmNumber { get; set; } = 1;

    [JsonPropertyName("assignments")]
    public Dictionary<string, string> Assignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
