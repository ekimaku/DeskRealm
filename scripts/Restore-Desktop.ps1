$ErrorActionPreference = "Stop"

$ConfigPath = Join-Path $env:APPDATA "DeskRealm\deskrealm.config.json"
if (-not (Test-Path $ConfigPath)) {
    throw "DeskRealm config was not found: $ConfigPath"
}

$Config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
$Original = [string]$Config.originalDesktopPath
if ([string]::IsNullOrWhiteSpace($Original)) {
    throw "originalDesktopPath is missing from $ConfigPath"
}
if (-not (Test-Path $Original)) {
    throw "Original Desktop was not found: $Original"
}

$code = @"
using System;
using System.Runtime.InteropServices;

public static class KnownFolderRestore
{
    private static Guid Desktop = new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHSetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, string pszPath);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, int uFlags, string dwItem1, IntPtr dwItem2);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static void Restore(string path)
    {
        var hr = SHSetKnownFolderPath(ref Desktop, 0, IntPtr.Zero, path);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);

        SHChangeNotify(0x00001000, 0x0005 | 0x2000, path, IntPtr.Zero);
        SHChangeNotify(0x08000000, 0x0000 | 0x2000, IntPtr.Zero, IntPtr.Zero);
    }
}
"@

Add-Type -TypeDefinition $code
[KnownFolderRestore]::Restore($Original)
Write-Host "Desktop restored: $Original" -ForegroundColor Green
