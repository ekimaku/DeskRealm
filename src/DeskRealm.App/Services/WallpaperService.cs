using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace DeskRealm.App.Services;

/// <summary>
/// Windows 11 per-virtual-desktop wallpaper adapter.
///
/// DeskRealm intentionally avoids internal virtual-desktop COM. Instead it writes the
/// wallpaper value kept by Windows for a known virtual-desktop GUID and, only after the
/// target desktop is confirmed active, asks Windows to refresh the visible background via
/// SystemParametersInfoW. Every failure is surfaced to the caller; there is no hidden
/// fallback to a different desktop or wallpaper.
/// </summary>
internal sealed class WallpaperService
{
    private const string VirtualDesktopsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private const string DesktopSubkeyRoot = @"Desktops";
    private const string WallpaperValueName = "Wallpaper";
    private const uint SpiSetDesktopWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendWinIniChange = 0x0002;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".dib", ".gif", ".jfif", ".jpe", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp"
    };

    public static IReadOnlyList<string> SupportedFileExtensions { get; } = SupportedExtensions.OrderBy(item => item).ToArray();

    private readonly FileLogger _logger;

    public WallpaperService(FileLogger logger) => _logger = logger;

    /// <summary>
    /// Reads the native per-virtual-desktop Registry value without applying, creating or
    /// modifying anything. This gives Realm Studio a truthful Windows → DeskRealm sync path.
    /// </summary>
    public string? TryGetNativeAssignment(Guid desktopId)
    {
        if (desktopId == Guid.Empty) throw new InvalidOperationException("Cannot read a wallpaper for an empty Windows virtual-desktop GUID.");
        using var virtualDesktops = Registry.CurrentUser.OpenSubKey(VirtualDesktopsKey, writable: false);
        if (virtualDesktops is null) return null;

        foreach (var formattedId in new[] { desktopId.ToString("B"), desktopId.ToString("D"), desktopId.ToString("N") })
        {
            using var desktopKey = virtualDesktops.OpenSubKey($@"{DesktopSubkeyRoot}\{formattedId}", writable: false);
            if (desktopKey?.GetValue(WallpaperValueName) is not string value || string.IsNullOrWhiteSpace(value)) continue;
            return Path.GetFullPath(value.Trim());
        }

        return null;
    }

    /// <summary>
    /// Compares paths first, then SHA-256 content when both files are readable. Different
    /// paths to the same image should not continuously re-import a wallpaper into AppData.
    /// </summary>
    public bool RefersToSameImage(string? firstPath, string? secondPath)
    {
        if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath)) return false;
        var first = Path.GetFullPath(firstPath.Trim());
        var second = Path.GetFullPath(secondPath.Trim());
        if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase)) return true;
        if (!File.Exists(first) || !File.Exists(second)) return false;

        try
        {
            using var firstStream = File.OpenRead(first);
            using var secondStream = File.OpenRead(second);
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(firstStream), SHA256.HashData(secondStream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn($"Wallpaper identity comparison could not read one source: {ex.Message}");
            return false;
        }
    }

    public RealmWallpaper ImportManagedCopy(Guid desktopId, string sourcePath)
    {
        if (desktopId == Guid.Empty) throw new InvalidOperationException("Cannot import a wallpaper for an empty Windows virtual-desktop GUID.");
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new InvalidOperationException("Wallpaper source path is empty.");
        var fullSource = Path.GetFullPath(sourcePath.Trim());
        if (!File.Exists(fullSource)) throw new FileNotFoundException("Wallpaper file was not found.", fullSource);

        var extension = Path.GetExtension(fullSource);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                "Wallpaper format is not supported by DeskRealm. Choose a standard image file: " +
                string.Join(", ", SupportedExtensions.OrderBy(item => item)) + ".");
        }

        Directory.CreateDirectory(AppPaths.WallpapersRoot);
        using var stream = File.OpenRead(fullSource);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        var target = Path.Combine(AppPaths.WallpapersRoot, $"{desktopId:N}-{hash[..16]}{extension.ToLowerInvariant()}");
        if (!File.Exists(target))
        {
            File.Copy(fullSource, target, overwrite: false);
        }

        _logger.Info($"Realm wallpaper imported: desktop={desktopId:B}, source='{fullSource}', managed='{target}', sha256={hash}.");
        return new RealmWallpaper
        {
            ManagedPath = target,
            SourceFileName = Path.GetFileName(fullSource),
            UpdatedAt = DateTimeOffset.Now
        };
    }

    public void PersistNativeAssignment(Guid desktopId, RealmWallpaper wallpaper)
    {
        var managedPath = ValidateManagedWallpaper(wallpaper);
        using var desktopKey = OpenDesktopKey(desktopId, writable: true);
        desktopKey.SetValue(WallpaperValueName, managedPath, RegistryValueKind.String);
        _logger.Info($"Native per-desktop wallpaper value stored: desktop={desktopId:B}, value='{managedPath}'.");
    }

    public void ApplyForActiveDesktop(Guid desktopId, RealmWallpaper wallpaper)
    {
        var managedPath = ValidateManagedWallpaper(wallpaper);
        PersistNativeAssignment(desktopId, wallpaper);

        if (!SystemParametersInfoW(SpiSetDesktopWallpaper, 0, managedPath, SpifUpdateIniFile | SpifSendWinIniChange))
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error,
                $"SystemParametersInfoW(SPI_SETDESKWALLPAPER) failed for desktop {desktopId:B} and wallpaper '{managedPath}'. Win32Error={error}.");
        }

        _logger.Info($"Native wallpaper applied after virtual-desktop commit: desktop={desktopId:B}, wallpaper='{managedPath}'.");
    }

    public void ClearNativeAssignment(Guid desktopId)
    {
        using var desktopKey = OpenDesktopKey(desktopId, writable: true);
        desktopKey.DeleteValue(WallpaperValueName, throwOnMissingValue: false);
        _logger.Info($"Native per-desktop wallpaper value removed: desktop={desktopId:B}.");
    }

    private static string ValidateManagedWallpaper(RealmWallpaper? wallpaper)
    {
        if (wallpaper is null || string.IsNullOrWhiteSpace(wallpaper.ManagedPath))
        {
            throw new InvalidOperationException("Realm wallpaper metadata is missing its managed file path.");
        }

        var path = Path.GetFullPath(wallpaper.ManagedPath.Trim());
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The managed realm wallpaper is missing. Re-select the image in Realm Studio before switching this realm.",
                path);
        }

        return path;
    }

    private static RegistryKey OpenDesktopKey(Guid desktopId, bool writable)
    {
        if (desktopId == Guid.Empty) throw new InvalidOperationException("Cannot open native wallpaper storage for an empty desktop GUID.");
        using var virtualDesktops = Registry.CurrentUser.OpenSubKey(VirtualDesktopsKey, writable)
            ?? throw new InvalidOperationException($"Windows virtual-desktop registry storage was not found: HKCU\\{VirtualDesktopsKey}");

        foreach (var formattedId in new[] { desktopId.ToString("B"), desktopId.ToString("D"), desktopId.ToString("N") })
        {
            var relativePath = $@"{DesktopSubkeyRoot}\{formattedId}";
            var existing = virtualDesktops.OpenSubKey(relativePath, writable);
            if (existing is not null) return existing;
        }

        if (!writable)
        {
            throw new InvalidOperationException($"Windows does not expose registry storage for virtual desktop {desktopId:B}.");
        }

        // This can only happen when Windows has not materialized the metadata subkey yet.
        // The GUID still comes from VirtualDesktopIDs, so DeskRealm creates the same native
        // Desktop metadata branch Windows uses rather than manufacturing a foreign identity.
        return virtualDesktops.CreateSubKey($@"{DesktopSubkeyRoot}\{desktopId:B}", writable: true)
            ?? throw new InvalidOperationException($"Could not create Windows wallpaper storage for virtual desktop {desktopId:B}.");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, string pvParam, uint fWinIni);
}
