using System.Text.Json.Serialization;

namespace DeskRealm.App.Services;

internal sealed class RealmConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 6;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("pollIntervalMs")]
    public int PollIntervalMs { get; set; } = 750;

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

    [JsonPropertyName("iconLayoutSettleDelayMs")]
    public int IconLayoutSettleDelayMs { get; set; } = 500;

    [JsonPropertyName("iconLayoutAutoSaveEnabled")]
    public bool IconLayoutAutoSaveEnabled { get; set; } = false;

    [JsonPropertyName("iconLayoutAutoSaveIntervalMs")]
    public int IconLayoutAutoSaveIntervalMs { get; set; } = 60000;

    [JsonPropertyName("iconLayoutWorkerTimeoutMs")]
    public int IconLayoutWorkerTimeoutMs { get; set; } = 8000;

    [JsonPropertyName("iconLayoutDisplayTopologyGuardEnabled")]
    public bool IconLayoutDisplayTopologyGuardEnabled { get; set; } = true;

    [JsonPropertyName("iconLayoutDisplayTopologySettleDelayMs")]
    public int IconLayoutDisplayTopologySettleDelayMs { get; set; } = 1200;

    [JsonPropertyName("iconLayoutSwitchRestoreDelayMs")]
    public int IconLayoutSwitchRestoreDelayMs { get; set; } = 1400;

    [JsonPropertyName("iconLayoutRestoreRetryCount")]
    public int IconLayoutRestoreRetryCount { get; set; } = 2;

    [JsonPropertyName("iconLayoutRestoreRetryDelayMs")]
    public int IconLayoutRestoreRetryDelayMs { get; set; } = 450;


    [JsonPropertyName("desktopHotkeysEnabled")]
    public bool DesktopHotkeysEnabled { get; set; } = true;

    [JsonPropertyName("desktopHotkeys")]
    public Dictionary<string, string> DesktopHotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1"] = "Win+Shift+W",
        ["2"] = "Win+Shift+X",
        ["3"] = "Win+Shift+C",
        ["4"] = "Win+Shift+V",
        ["5"] = "Win+Shift+B",
        ["6"] = "Win+Shift+N"
    };

    [JsonPropertyName("hotkeyInitialDelayMs")]
    public int HotkeyInitialDelayMs { get; set; } = 180;

    [JsonPropertyName("hotkeySwitchStepDelayMs")]
    public int HotkeySwitchStepDelayMs { get; set; } = 160;

    [JsonPropertyName("hotkeySwitchSettleTimeoutMs")]
    public int HotkeySwitchSettleTimeoutMs { get; set; } = 3000;

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
