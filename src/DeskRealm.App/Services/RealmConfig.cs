using System.Text.Json.Serialization;

namespace DeskRealm.App.Services;

internal sealed class RealmConfig
{
    public const int CurrentVersion = 18;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("restoreDesktopOnExit")]
    public bool RestoreDesktopOnExit { get; set; } = true;

    [JsonPropertyName("rejectOneDriveDesktop")]
    public bool RejectOneDriveDesktop { get; set; } = true;

    [JsonPropertyName("realmRenameApplyMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RealmRenameApplyMode RealmRenameApplyMode { get; set; } = RealmRenameApplyMode.Ask;

    [JsonPropertyName("initialDesktopImportPromptEnabled")]
    public bool InitialDesktopImportPromptEnabled { get; set; } = true;

    [JsonPropertyName("initialDesktopImportPromptCompleted")]
    public bool InitialDesktopImportPromptCompleted { get; set; }

    [JsonPropertyName("realmNameMaxLength")]
    public int RealmNameMaxLength { get; set; } = 80;

    [JsonPropertyName("iconLayoutPersistenceEnabled")]
    public bool IconLayoutPersistenceEnabled { get; set; } = true;

    [JsonPropertyName("iconLayoutWorkerTimeoutMs")]
    public int IconLayoutWorkerTimeoutMs { get; set; } = 8000;

    [JsonPropertyName("iconLayoutDisplayTopologyGuardEnabled")]
    public bool IconLayoutDisplayTopologyGuardEnabled { get; set; } = true;

    [JsonPropertyName("shellViewReadyTimeoutMs")]
    public int ShellViewReadyTimeoutMs { get; set; } = 5000;

    [JsonPropertyName("iconLayoutRestoreVerificationTimeoutMs")]
    public int IconLayoutRestoreVerificationTimeoutMs { get; set; } = 1400;

    [JsonPropertyName("desktopHotkeysEnabled")]
    public bool DesktopHotkeysEnabled { get; set; } = true;

    // One-time v0.6/v0.7-pre-GUID import payload. It is read only when an old
    // config still needs its number-based bindings mapped to live desktop GUIDs;
    // it is then cleared and omitted from subsequent saves.
    [JsonPropertyName("desktopHotkeys")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? LegacyDesktopHotkeys { get; set; }

    [JsonPropertyName("realmHotkeys")]
    public Dictionary<string, string> RealmHotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("realmProfiles")]
    public Dictionary<string, RealmProfile> RealmProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("realmWallpapers")]
    public Dictionary<string, RealmWallpaper> RealmWallpapers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("archivedRealmProfiles")]
    public Dictionary<string, ArchivedRealmProfile> ArchivedRealmProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("lockedIconLayouts")]
    public Dictionary<string, bool> LockedIconLayouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("lockedRealms")]
    public Dictionary<string, bool> LockedRealms { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("lockedIconLayoutVariants")]
    public Dictionary<string, bool> LockedIconLayoutVariants { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("hotkeyModifierReleaseTimeoutMs")]
    public int HotkeyModifierReleaseTimeoutMs { get; set; } = 1200;

    [JsonPropertyName("desktopStepConfirmationTimeoutMs")]
    public int DesktopStepConfirmationTimeoutMs { get; set; } = 3000;

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    // Controls only Realm Studio visibility after DeskRealm launches. The tray/runtime
    // still start normally; first-run import always remains visible. Defaults to the
    // established behavior of launching directly into the notification area.
    [JsonPropertyName("startMinimized")]
    public bool StartMinimized { get; set; } = true;

    [JsonPropertyName("originalDesktopPath")]
    public string? OriginalDesktopPath { get; set; }

    [JsonPropertyName("realmsRoot")]
    public string? RealmsRoot { get; set; }

    [JsonPropertyName("assignments")]
    public Dictionary<string, string> Assignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal static Dictionary<string, string> CreateLegacyDefaultDesktopHotkeys()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["1"] = "Win+Shift+X",
            ["2"] = "Win+Shift+C",
            ["3"] = "Win+Shift+B",
            ["4"] = "Win+Shift+N"
        };
    }
}
