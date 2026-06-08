using System.Runtime.InteropServices;

namespace DeskRealm.App.Services;

internal sealed class ShellRefreshService
{
    private const int SHCNE_UPDATEDIR = 0x00001000;
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_PATHW = 0x0005;
    private const int SHCNF_IDLIST = 0x0000;
    private const int SHCNF_FLUSHNOWAIT = 0x2000;

    private const int HWND_BROADCAST = 0xffff;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    private readonly FileLogger _logger;

    public ShellRefreshService(FileLogger logger) => _logger = logger;

    public void RefreshDesktop(string path)
    {
        SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, path, IntPtr.Zero);
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSHNOWAIT, IntPtr.Zero, IntPtr.Zero);

        _ = SendMessageTimeout(
            new IntPtr(HWND_BROADCAST),
            WM_SETTINGCHANGE,
            IntPtr.Zero,
            "Environment",
            SMTO_ABORTIFHUNG,
            2000,
            out _);

        _logger.Info($"Shell refresh requested for {path}");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, int uFlags, string? dwItem1, IntPtr dwItem2);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        string? lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);
}
