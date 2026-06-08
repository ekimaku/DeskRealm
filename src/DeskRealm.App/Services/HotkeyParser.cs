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
            throw new InvalidOperationException($"Hotkey vide pour le bureau {desktopNumber}.");
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            throw new InvalidOperationException($"Hotkey invalide '{text}'. Format attendu, exemple : Win+Shift+W.");
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
                throw new InvalidOperationException($"Hotkey invalide '{text}' : plusieurs touches principales détectées ({keyToken}, {part}).");
            }

            keyToken = part;
        }

        if (modifiers == 0)
        {
            throw new InvalidOperationException($"Hotkey invalide '{text}' : aucun modificateur détecté.");
        }

        if (keyToken is null)
        {
            throw new InvalidOperationException($"Hotkey invalide '{text}' : touche principale absente.");
        }

        var virtualKey = ParseVirtualKey(keyToken, text);
        return new HotkeyBinding(desktopNumber, text.Trim(), modifiers, virtualKey);
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
            return (uint)key;
        }

        throw new InvalidOperationException($"Hotkey invalide '{fullText}' : touche principale inconnue '{keyToken}'.");
    }
}
