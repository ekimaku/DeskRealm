using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeskRealm.App.Services;

internal sealed class ShellRefreshService
{
    private const int SHCNE_UPDATEDIR = 0x00001000;
    private const int SHCNF_PATHW = 0x0005;
    private const int SHCNF_FLUSHNOWAIT = 0x2000;

    private readonly FileLogger _logger;

    public ShellRefreshService(FileLogger logger) => _logger = logger;

    public void RefreshDesktop(string path)
    {
        // Notify the Shell about the exact directory that changed. Do not broadcast
        // a synchronous settings message to every top-level window: that is a system-settings signal,
        // its timeout is per receiving window, and it created a measured ~1 second stall.
        // Explorer readiness remains the strict state-based proof that the view followed.
        var stopwatch = Stopwatch.StartNew();
        SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, path, IntPtr.Zero);
        stopwatch.Stop();

        _logger.Info(
            $"[PERF] Targeted Shell directory notification requested for {path} " +
            $"in {stopwatch.Elapsed.TotalMilliseconds:0.0} ms.");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, int uFlags, string? dwItem1, IntPtr dwItem2);
}
