namespace DeskRealm.App.Services;

internal static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    private static readonly IReadOnlyDictionary<string, uint> NamedKeys = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
    {
        ["Back"] = 0x08, ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Pause"] = 0x13, ["CapsLock"] = 0x14,
        ["Escape"] = 0x1B, ["Space"] = 0x20, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["End"] = 0x23, ["Home"] = 0x24, ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27,
        ["Down"] = 0x28, ["PrintScreen"] = 0x2C, ["Insert"] = 0x2D, ["Delete"] = 0x2E,
        ["NumPad0"] = 0x60, ["NumPad1"] = 0x61, ["NumPad2"] = 0x62, ["NumPad3"] = 0x63,
        ["NumPad4"] = 0x64, ["NumPad5"] = 0x65, ["NumPad6"] = 0x66, ["NumPad7"] = 0x67,
        ["NumPad8"] = 0x68, ["NumPad9"] = 0x69, ["Multiply"] = 0x6A, ["Add"] = 0x6B,
        ["Subtract"] = 0x6D, ["Decimal"] = 0x6E, ["Divide"] = 0x6F,
        ["NumLock"] = 0x90, ["Scroll"] = 0x91,
        ["OemSemicolon"] = 0xBA, ["OemPlus"] = 0xBB, ["OemComma"] = 0xBC, ["OemMinus"] = 0xBD,
        ["OemPeriod"] = 0xBE, ["OemQuestion"] = 0xBF, ["OemTilde"] = 0xC0,
        ["OemOpenBrackets"] = 0xDB, ["OemPipe"] = 0xDC, ["OemCloseBrackets"] = 0xDD, ["OemQuotes"] = 0xDE
    };

    public static HotkeyBinding Parse(Guid desktopId, string text)
    {
        if (desktopId == Guid.Empty)
        {
            throw new InvalidOperationException("A realm hotkey requires a non-empty Windows desktop GUID.");
        }

        return ParseCore(text, label: $"realm {desktopId:B}");
    }

    public static HotkeyBinding ParseCore(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"Empty hotkey for {label}.");
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
            if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase)) { modifiers |= ModWin; continue; }
            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) { modifiers |= ModShift; continue; }
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase)) { modifiers |= ModControl; continue; }
            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) { modifiers |= ModAlt; continue; }
            if (keyToken is not null) { throw new InvalidOperationException($"Invalid hotkey '{text}': multiple main keys detected ({keyToken}, {part})."); }
            keyToken = part;
        }

        if (modifiers == 0) { throw new InvalidOperationException($"Invalid hotkey '{text}': no modifier detected."); }
        if (CountModifiers(modifiers) > 2) { throw new InvalidOperationException($"Invalid hotkey '{text}': use one or two modifiers only, then one main key."); }
        if (keyToken is null) { throw new InvalidOperationException($"Invalid hotkey '{text}': main key missing."); }

        var virtualKey = ParseVirtualKey(keyToken, text);
        return new HotkeyBinding(FormatHotkeyText(modifiers, virtualKey), modifiers, virtualKey);
    }

    public static string FormatHotkeyText(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        parts.Add(FormatVirtualKey(virtualKey));
        return string.Join("+", parts);
    }

    /// <summary>
    /// Formats modifiers while a UI capture is still waiting for its main key.
    /// This deliberately does not call <see cref="FormatHotkeyText"/>, because a
    /// modifier-only preview is not a complete, registrable hotkey.
    /// </summary>
    public static string FormatModifierPreview(uint modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        return string.Join("+", parts);
    }

    /// <summary>
    /// Returns true for virtual keys that can only be used as modifiers and must
    /// never become the main key of a DeskRealm shortcut.
    /// </summary>
    public static bool IsModifierVirtualKey(uint virtualKey)
    {
        return virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;
    }

    public static string FormatVirtualKey(uint virtualKey)
    {
        if ((virtualKey is >= 'A' and <= 'Z') || (virtualKey is >= '0' and <= '9')) return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x70 and <= 0x87) return "F" + (virtualKey - 0x6F);
        return NamedKeys.FirstOrDefault(pair => pair.Value == virtualKey).Key
               ?? throw new InvalidOperationException($"Virtual key 0x{virtualKey:X2} cannot be represented in DeskRealm's strict hotkey grammar.");
    }

    public static int CountModifiers(uint modifiers)
    {
        var count = 0;
        if ((modifiers & ModWin) != 0) count++;
        if ((modifiers & ModControl) != 0) count++;
        if ((modifiers & ModAlt) != 0) count++;
        if ((modifiers & ModShift) != 0) count++;
        return count;
    }

    private static uint ParseVirtualKey(string keyToken, string fullText)
    {
        if (keyToken.Length == 1)
        {
            var c = char.ToUpperInvariant(keyToken[0]);
            if ((c is >= 'A' and <= 'Z') || (c is >= '0' and <= '9')) return c;
        }

        if (keyToken.StartsWith('F') && int.TryParse(keyToken[1..], out var functionNumber) && functionNumber is >= 1 and <= 24)
        {
            return (uint)(0x6F + functionNumber);
        }

        if (NamedKeys.TryGetValue(keyToken, out var virtualKey)) return virtualKey;
        throw new InvalidOperationException($"Invalid hotkey '{fullText}': unknown main key '{keyToken}'.");
    }
}
