using System.Runtime.InteropServices;

namespace DeskRealm.App.Services;

internal sealed class KeyboardInputService
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_RIGHT = 0x27;

    private readonly FileLogger _logger;

    public KeyboardInputService(FileLogger logger) => _logger = logger;

    public void SwitchVirtualDesktopStep(int direction)
    {
        if (direction == 0)
        {
            return;
        }

        var arrow = direction > 0 ? VK_RIGHT : VK_LEFT;
        var arrowName = direction > 0 ? "Right" : "Left";
        var inputSize = Marshal.SizeOf<INPUT>();
        _logger.Info($"Keyboard navigation: Win+Ctrl+{arrowName} (INPUT cbSize={inputSize}).");

        var inputs = new[]
        {
            KeyDown(VK_LWIN),
            KeyDown(VK_CONTROL),
            KeyDown(arrow),
            KeyUp(arrow),
            KeyUp(VK_CONTROL),
            KeyUp(VK_LWIN)
        };

        var sent = SendInput((uint)inputs.Length, inputs, inputSize);
        if (sent != (uint)inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SendInput failed pour Win+Ctrl+{arrowName}. Sent={sent}/{inputs.Length}, Win32Error={error}, INPUT cbSize={inputSize}.");
        }
    }

    private static INPUT KeyDown(ushort key) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = key,
                wScan = 0,
                dwFlags = 0,
                time = 0,
                dwExtraInfo = UIntPtr.Zero
            }
        }
    };

    private static INPUT KeyUp(ushort key) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = key,
                wScan = 0,
                dwFlags = KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = UIntPtr.Zero
            }
        }
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    // IMPORTANT: INPUT contains a native union whose size is the largest of
    // MOUSEINPUT, KEYBDINPUT and HARDWAREINPUT. If the C# union only declares
    // KEYBDINPUT, Marshal.SizeOf<INPUT>() is too small on x64 and SendInput
    // fails with ERROR_INVALID_PARAMETER (87). Keep every union member here.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
