// DeskRealm-RealmStudio-Schema: v0.7.0
using DeskRealm.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.System;

namespace DeskRealm.App.Controls;

/// <summary>
/// Reusable keyboard-capture field for DeskRealm shortcuts. The control owns only the draft
/// keyboard value and capture lifecycle; surrounding surfaces provide their own Save/Cancel UI.
/// </summary>
internal sealed class HotkeyCaptureField : Grid
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;

    private const string CaptureInstructions =
        "Click the field, hold one or two modifiers, then press a main key. " +
        "Esc cancels the current capture. Backspace or Delete clears the draft hotkey.";

    private readonly TextBox _captureBox;
    private readonly string? _initialValue;
    private string? _value;
    private string? _valueBeforeCapture;
    private bool _capturing;
    private bool _captureRequestedWhenLoaded;
    private bool _isLoaded;

    public HotkeyCaptureField(string? initialHotkey)
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        _initialValue = Normalize(initialHotkey);
        _value = _initialValue;

        _captureBox = new TextBox
        {
            IsReadOnly = true,
            IsTabStop = true,
            Text = DisplayValue(_value),
            PlaceholderText = "Click to record a global realm hotkey",
            Style = Resource<Style>("DeskRealmTextInput"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 0
        };
        ToolTipService.SetToolTip(_captureBox, CaptureInstructions);
        _captureBox.PointerPressed += OnPointerPressed;
        _captureBox.KeyDown += OnKeyDown;
        _captureBox.KeyUp += OnKeyUp;
        _captureBox.LostFocus += OnLostFocus;
        Children.Add(_captureBox);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string? Value => _value;

    /// <summary>
    /// Arms capture as soon as the control belongs to the live visual tree. This lets an inline
    /// VCard editor begin recording from one explicit pencil click without a second pointer click.
    /// </summary>
    public void StartCaptureWhenReady()
    {
        if (_capturing) return;

        _captureRequestedWhenLoaded = true;
        if (_isLoaded)
        {
            StartCaptureAndFocus();
        }
    }

    /// <summary>
    /// Restores the hotkey that was present when this edit field was opened. Reset never commits,
    /// clears or re-registers a global hotkey; the surrounding surface still owns Save.
    /// </summary>
    public void ResetToInitialValue()
    {
        _captureRequestedWhenLoaded = false;
        _capturing = false;
        _valueBeforeCapture = null;
        _value = _initialValue;
        _captureBox.Text = DisplayValue(_value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        if (_captureRequestedWhenLoaded)
        {
            StartCaptureAndFocus();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        StartCaptureWhenReady();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturing) StartCaptureWhenReady();
        e.Handled = true;
        var virtualKey = (uint)e.Key;

        if (e.Key == VirtualKey.Escape)
        {
            CancelCapture();
            return;
        }

        var modifiers = ReadActiveModifiers();
        if ((e.Key == VirtualKey.Back || e.Key == VirtualKey.Delete) && modifiers == 0)
        {
            ClearDraft();
            return;
        }

        if (HotkeyParser.IsModifierVirtualKey(virtualKey))
        {
            ShowModifierPreview(modifiers);
            return;
        }

        if (modifiers == 0)
        {
            _captureBox.Text = "Add Win / Ctrl / Alt / Shift…";
            return;
        }

        if (HotkeyParser.CountModifiers(modifiers) > 2)
        {
            _captureBox.Text = "Use one or two modifiers…";
            return;
        }

        try
        {
            var candidate = HotkeyParser.FormatHotkeyText(modifiers, virtualKey);
            _value = HotkeyParser.ParseCore(candidate, "captured realm hotkey").Text;
            _capturing = false;
            _valueBeforeCapture = null;
            _captureBox.Text = _value;
        }
        catch
        {
            _captureBox.Text = "Unsupported key…";
        }
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;
        if (HotkeyParser.IsModifierVirtualKey((uint)e.Key) && ReadActiveModifiers() == 0)
        {
            CancelCapture();
        }
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_capturing) CancelCapture();
    }

    private void StartCaptureAndFocus()
    {
        _captureRequestedWhenLoaded = false;
        BeginCapture();
        _captureBox.Focus(FocusState.Programmatic);
    }

    private void BeginCapture()
    {
        if (_capturing) return;
        _capturing = true;
        _valueBeforeCapture = _value;
        _captureBox.Text = "Waiting input...";
    }

    private void ShowModifierPreview(uint modifiers)
    {
        if (modifiers == 0)
        {
            _captureBox.Text = "Waiting input...";
            return;
        }
        if (HotkeyParser.CountModifiers(modifiers) > 2)
        {
            _captureBox.Text = "Use one or two modifiers…";
            return;
        }

        _captureBox.Text = HotkeyParser.FormatModifierPreview(modifiers) + "+…";
    }

    // DeskRealm-HotkeyCapture-CancelContract: Escape, modifier release and focus loss preserve the prior binding.
    private void CancelCapture()
    {
        _captureRequestedWhenLoaded = false;
        _capturing = false;
        _value = _valueBeforeCapture;
        _valueBeforeCapture = null;
        _captureBox.Text = DisplayValue(_value);
    }

    private void ClearDraft()
    {
        _captureRequestedWhenLoaded = false;
        _capturing = false;
        _valueBeforeCapture = null;
        _value = null;
        _captureBox.Text = DisplayValue(null);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return HotkeyParser.ParseCore(value.Trim(), "initial realm hotkey").Text;
    }

    private static string DisplayValue(string? value) => value ?? "No global hotkey";

    private static uint ReadActiveModifiers()
    {
        uint modifiers = 0;
        if (IsDown(VkLWin) || IsDown(VkRWin)) modifiers |= HotkeyParser.ModWin;
        if (IsDown(VkControl) || IsDown(VkLControl) || IsDown(VkRControl)) modifiers |= HotkeyParser.ModControl;
        if (IsDown(VkMenu) || IsDown(VkLMenu) || IsDown(VkRMenu)) modifiers |= HotkeyParser.ModAlt;
        if (IsDown(VkShift) || IsDown(VkLShift) || IsDown(VkRShift)) modifiers |= HotkeyParser.ModShift;
        return modifiers;
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    private static T Resource<T>(string key) where T : class
        => (Application.Current.Resources[key] as T) ?? throw new InvalidOperationException($"DeskRealm application resource '{key}' is missing.");

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
