using System.Runtime.InteropServices;

namespace DeskRealm.App.Services;

internal sealed class KnownFolderService
{
    private static readonly Guid FolderIdDesktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");

    private static Guid GetDesktopFolderId() => FolderIdDesktop;
    private readonly FileLogger _logger;

    public KnownFolderService(FileLogger logger) => _logger = logger;

    public string GetDesktopPath()
    {
        var folderIdDesktop = GetDesktopFolderId();
        var hr = SHGetKnownFolderPath(ref folderIdDesktop, 0, IntPtr.Zero, out var pathPtr);
        if (hr != 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        try
        {
            return Marshal.PtrToStringUni(pathPtr)
                ?? throw new InvalidOperationException("SHGetKnownFolderPath returned a null path.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }

    public void SetDesktopPath(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath))
        {
            throw new ArgumentException("The new Desktop path is empty.", nameof(newPath));
        }

        if (!Directory.Exists(newPath))
        {
            throw new DirectoryNotFoundException($"Target Desktop folder does not exist : {newPath}");
        }

        var folderIdDesktop = GetDesktopFolderId();
        var hr = SHSetKnownFolderPath(ref folderIdDesktop, 0, IntPtr.Zero, newPath);
        if (hr != 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        _logger.Info($"Known Folder Desktop -> {newPath}");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHSetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, string pszPath);
}
