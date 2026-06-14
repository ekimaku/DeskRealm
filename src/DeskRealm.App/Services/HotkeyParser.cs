using System.Windows.Forms;

namespace DeskRealm.App.Services;

internal static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    public static HotkeyBinding Parse(int desktopNumber, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"Empty hotkey for desktop {desktopNumber}.");
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            throw new InvalidOperationException($"Invalid hotkey '{text}'. Format expected, example: Win+Shift+X.");
        }

        uint modifiers = 0;
        string? keyToken = null;

        foreach (var part in parts)
        {
            if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModWin;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModShift;
                continue;
            }

            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModControl;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModAlt;
                continue;
            }

            if (keyToken is not null)
            {
                throw new InvalidOperationException($"Invalid hotkey '{text}': multiple main keys detected ({keyToken}, {part}).");
            }

            keyToken = part;
        }

        if (modifiers == 0)
        {
            throw new InvalidOperationException($"Invalid hotkey '{text}': no modifier detected.");
        }

        if (CountModifiers(modifiers) > 2)
        {
            throw new InvalidOperationException($"Invalid hotkey '{text}': use one or two modifiers only, then one main key.");
        }

        if (keyToken is null)
        {
            throw new InvalidOperationException($"Invalid hotkey '{text}': main key missing.");
        }

        var virtualKey = ParseVirtualKey(keyToken, text);
        var normalizedText = FormatHotkeyText(modifiers, virtualKey);
        return new HotkeyBinding(desktopNumber, normalizedText, modifiers, virtualKey);
    }

    public static string FormatHotkeyText(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & ModWin) != 0)
        {
            parts.Add("Win");
        }

        if ((modifiers & ModControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModShift) != 0)
        {
            parts.Add("Shift");
        }

        parts.Add(FormatVirtualKey(virtualKey));
        return string.Join("+", parts);
    }

    public static string FormatVirtualKey(uint virtualKey)
    {
        if (virtualKey is >= 'A' and <= 'Z')
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= '0' and <= '9')
        {
            return ((char)virtualKey).ToString();
        }

        return ((Keys)virtualKey).ToString();
    }

    public static int CountModifiers(uint modifiers)
    {
        var count = 0;
        if ((modifiers & ModWin) != 0)
        {
            count++;
        }

        if ((modifiers & ModControl) != 0)
        {
            count++;
        }

        if ((modifiers & ModAlt) != 0)
        {
            count++;
        }

        if ((modifiers & ModShift) != 0)
        {
            count++;
        }

        return count;
    }

    private static uint ParseVirtualKey(string keyToken, string fullText)
    {
        if (keyToken.Length == 1)
        {
            var c = char.ToUpperInvariant(keyToken[0]);
            if (c is >= 'A' and <= 'Z')
            {
                return (uint)c;
            }

            if (c is >= '0' and <= '9')
            {
                return (uint)c;
            }
        }

        if (Enum.TryParse<Keys>(keyToken, ignoreCase: true, out var key))
        {
            if (IsModifierOnlyKey(key))
            {
                throw new InvalidOperationException($"Invalid hotkey '{fullText}': main key cannot be a modifier.");
            }

            return (uint)key;
        }

        throw new InvalidOperationException($"Invalid hotkey '{fullText}': unknown main key '{keyToken}'.");
    }

    private static bool IsModifierOnlyKey(Keys key)
    {
        return key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.Control or Keys.Shift or Keys.Alt;
    }
}
