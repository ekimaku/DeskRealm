using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeskRealm.App.Services;

internal static class DisplayTopologyService
{
    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITORINFOF_PRIMARY = 0x00000001;

    private static readonly JsonSerializerOptions StableJsonOptions = new() { WriteIndented = false };

    public static DisplayTopologySnapshot Capture()
    {
        var screens = new List<DisplayScreenInfo>();
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new MONITORINFOEXW { cbSize = Marshal.SizeOf<MONITORINFOEXW>() };
                if (!GetMonitorInfoW(monitor, ref info))
                {
                    throw new InvalidOperationException($"Could not read monitor information. Win32Error={Marshal.GetLastWin32Error()}.");
                }

                var dpi = TryGetEffectiveDpi(monitor);
                var bounds = info.rcMonitor;
                var working = info.rcWork;
                screens.Add(new DisplayScreenInfo
                {
                    DeviceName = info.szDevice?.TrimEnd('\0') ?? string.Empty,
                    Primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                    BoundsX = bounds.left,
                    BoundsY = bounds.top,
                    BoundsWidth = bounds.right - bounds.left,
                    BoundsHeight = bounds.bottom - bounds.top,
                    WorkingX = working.left,
                    WorkingY = working.top,
                    WorkingWidth = working.right - working.left,
                    WorkingHeight = working.bottom - working.top,
                    EffectiveDpiX = dpi.dpiX,
                    EffectiveDpiY = dpi.dpiY,
                    ScalePercent = dpi.dpiX <= 0 ? 0 : (int)Math.Round(dpi.dpiX * 100.0 / 96.0),
                    Orientation = (bounds.right - bounds.left) >= (bounds.bottom - bounds.top) ? "landscape" : "portrait"
                });
                return true;
            }, IntPtr.Zero))
        {
            throw new InvalidOperationException($"Display topology unavailable: EnumDisplayMonitors failed. Win32Error={Marshal.GetLastWin32Error()}.");
        }

        screens = screens.OrderBy(s => s.DeviceName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.BoundsX).ThenBy(s => s.BoundsY).ToList();
        if (screens.Count == 0) throw new InvalidOperationException("Display topology unavailable: Windows did not return any active screen.");

        var minX = screens.Min(s => s.BoundsX);
        var minY = screens.Min(s => s.BoundsY);
        var maxX = screens.Max(s => s.BoundsX + s.BoundsWidth);
        var maxY = screens.Max(s => s.BoundsY + s.BoundsHeight);
        var snapshot = new DisplayTopologySnapshot
        {
            CapturedAt = DateTimeOffset.Now,
            VirtualBoundsX = minX,
            VirtualBoundsY = minY,
            VirtualBoundsWidth = maxX - minX,
            VirtualBoundsHeight = maxY - minY,
            Screens = screens
        };
        snapshot.FamilyKey = BuildFamilyKey(snapshot);
        snapshot.Key = BuildExactKey(snapshot);
        return snapshot;
    }

    private static (int dpiX, int dpiY) TryGetEffectiveDpi(nint monitor)
    {
        var hr = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY);
        return hr == 0 ? ((int)dpiX, (int)dpiY) : (0, 0);
    }

    private static string BuildExactKey(DisplayTopologySnapshot snapshot) => "display-topology-sha256:" + HashStableObject(new
    {
        snapshot.VirtualBoundsX, snapshot.VirtualBoundsY, snapshot.VirtualBoundsWidth, snapshot.VirtualBoundsHeight,
        Screens = snapshot.Screens.Select(s => new { s.DeviceName, s.Primary, s.BoundsX, s.BoundsY, s.BoundsWidth, s.BoundsHeight, s.WorkingX, s.WorkingY, s.WorkingWidth, s.WorkingHeight, s.EffectiveDpiX, s.EffectiveDpiY, s.ScalePercent, s.Orientation })
    });

    private static string BuildFamilyKey(DisplayTopologySnapshot snapshot) => "display-family-sha256:" + HashStableObject(new
    {
        ScreenCount = snapshot.Screens.Count,
        Screens = snapshot.Screens.Select(s => new { s.DeviceName, s.Primary })
    });

    private static string HashStableObject(object source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(source, StableJsonOptions)))).ToLowerInvariant();

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, nint lprcMonitor, nint dwData);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc callback, nint dwData);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFOEXW lpmi);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left; public int top; public int right; public int bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MONITORINFOEXW
    {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? szDevice;
    }
}
