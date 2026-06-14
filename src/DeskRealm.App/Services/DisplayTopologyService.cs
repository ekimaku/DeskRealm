using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeskRealm.App.Services;

internal static class DisplayTopologyService
{
    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int MDT_EFFECTIVE_DPI = 0;

    private static readonly JsonSerializerOptions StableJsonOptions = new()
    {
        WriteIndented = false
    };

    public static DisplayTopologySnapshot Capture()
    {
        var screens = Screen.AllScreens
            .Select(BuildScreenInfo)
            .OrderBy(s => s.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.BoundsX)
            .ThenBy(s => s.BoundsY)
            .ToList();

        if (screens.Count == 0)
        {
            throw new InvalidOperationException("Display topology unavailable: Windows did not return any active screen.");
        }

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

    private static DisplayScreenInfo BuildScreenInfo(Screen screen)
    {
        var bounds = screen.Bounds;
        var working = screen.WorkingArea;
        var dpi = TryGetEffectiveDpi(bounds);
        var orientation = bounds.Width >= bounds.Height ? "landscape" : "portrait";

        return new DisplayScreenInfo
        {
            DeviceName = screen.DeviceName,
            Primary = screen.Primary,
            BoundsX = bounds.X,
            BoundsY = bounds.Y,
            BoundsWidth = bounds.Width,
            BoundsHeight = bounds.Height,
            WorkingX = working.X,
            WorkingY = working.Y,
            WorkingWidth = working.Width,
            WorkingHeight = working.Height,
            EffectiveDpiX = dpi.dpiX,
            EffectiveDpiY = dpi.dpiY,
            ScalePercent = dpi.dpiX <= 0 ? 0 : (int)Math.Round(dpi.dpiX * 100.0 / 96.0),
            Orientation = orientation
        };
    }

    private static (int dpiX, int dpiY) TryGetEffectiveDpi(Rectangle bounds)
    {
        var center = new NativePoint(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        var monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return (0, 0);
        }

        var hr = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY);
        if (hr != 0)
        {
            return (0, 0);
        }

        return ((int)dpiX, (int)dpiY);
    }

    private static string BuildExactKey(DisplayTopologySnapshot snapshot)
    {
        var source = new
        {
            snapshot.VirtualBoundsX,
            snapshot.VirtualBoundsY,
            snapshot.VirtualBoundsWidth,
            snapshot.VirtualBoundsHeight,
            Screens = snapshot.Screens.Select(s => new
            {
                s.DeviceName,
                s.Primary,
                s.BoundsX,
                s.BoundsY,
                s.BoundsWidth,
                s.BoundsHeight,
                s.WorkingX,
                s.WorkingY,
                s.WorkingWidth,
                s.WorkingHeight,
                s.EffectiveDpiX,
                s.EffectiveDpiY,
                s.ScalePercent,
                s.Orientation
            })
        };

        return "display-topology-sha256:" + HashStableObject(source);
    }

    private static string BuildFamilyKey(DisplayTopologySnapshot snapshot)
    {
        var source = new
        {
            ScreenCount = snapshot.Screens.Count,
            Screens = snapshot.Screens.Select(s => new
            {
                s.DeviceName,
                s.Primary
            })
        };

        return "display-family-sha256:" + HashStableObject(source);
    }

    private static string HashStableObject(object source)
    {
        var json = JsonSerializer.Serialize(source, StableJsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, int dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public readonly int X;
        public readonly int Y;
    }
}
