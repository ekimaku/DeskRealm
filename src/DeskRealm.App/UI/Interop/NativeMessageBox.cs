using System.Runtime.InteropServices;

namespace DeskRealm.App.Interop;

internal static class NativeMessageBox
{
    internal enum Icon { Information, Warning, Error }
    public static void Show(string message, string title, Icon icon)
    {
        var style = icon switch
        {
            Icon.Information => 0x40u,
            Icon.Warning => 0x30u,
            Icon.Error => 0x10u,
            _ => 0u
        };
        _ = MessageBoxW(IntPtr.Zero, message, title, style | 0x00040000u);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string lpText, string lpCaption, uint uType);
}
