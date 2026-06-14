namespace DeskRealm.App.Services;

internal static class RealmFolderNameSanitizer
{
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string FromVirtualDesktopName(string virtualDesktopName, int maxLength)
    {
        if (maxLength is < 16 or > 120)
        {
            throw new InvalidOperationException("realmNameMaxLength invalid. Strict allowed value: 16 to 120 characters.");
        }

        var source = string.IsNullOrWhiteSpace(virtualDesktopName)
            ? "Desktop"
            : virtualDesktopName.Trim();

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = source.Select(ch => char.IsControl(ch) || invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim().Trim('.', ' ');

        while (sanitized.Contains("__", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("__", "_", StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new InvalidOperationException(
                $"The virtual desktop name '{virtualDesktopName}' does not produce any valid folder name.");
        }

        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength].Trim().Trim('.', ' ');
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new InvalidOperationException(
                $"The virtual desktop name '{virtualDesktopName}' is invalid after trimming to {maxLength} characters.");
        }

        if (ReservedWindowsNames.Contains(sanitized))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }
}
