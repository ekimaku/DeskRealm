namespace DeskRealm.App.Services;

internal sealed record HotkeyBinding(int DesktopNumber, string Text, uint Modifiers, uint VirtualKey);
