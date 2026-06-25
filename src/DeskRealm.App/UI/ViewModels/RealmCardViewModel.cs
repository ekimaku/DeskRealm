// DeskRealm-RealmStudio-Schema: v0.7.0
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskRealm.App.Controls;
using DeskRealm.App.Services;
using DeskRealm.App.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DeskRealm.App.ViewModels;

/// <summary>
/// Presentation and draft state for a single Realm Studio card. The card never mutates
/// Windows directly: all commands route back through the serialized runtime actions.
/// </summary>
internal sealed class RealmCardViewModel : ObservableObject
{
    private readonly Func<RealmCardViewModel, Task> _switchAction;
    private readonly Func<RealmCardViewModel, Task> _editAction;
    private readonly Func<RealmCardViewModel, Task> _deleteAction;
    private readonly Func<RealmCardViewModel, Task> _wallpaperAction;
    private readonly Func<RealmCardViewModel, Task> _hotkeyAction;
    private readonly Func<RealmCardViewModel, Task> _cancelHotkeyAction;
    private readonly Func<RealmCardViewModel, Task> _toggleRealmLockAction;
    private readonly Func<RealmCardViewModel, Task> _setDefaultRealmAction;

    private string _wallpaperPath;
    private string _wallpaperFileName;
    private string _wallpaperStatus;
    private ImageSource? _wallpaperPreview;
    private bool _hasWallpaperPreview;
    private string? _wallpaperDraftPath;
    private string? _hotkey;
    private bool _isHotkeyEditing;
    private HotkeyCaptureField? _hotkeyEditor;

    public RealmCardViewModel(
        RealmStudioRealmSnapshot snapshot,
        Func<RealmCardViewModel, Task> switchAction,
        Func<RealmCardViewModel, Task> editAction,
        Func<RealmCardViewModel, Task> deleteAction,
        Func<RealmCardViewModel, Task> wallpaperAction,
        Func<RealmCardViewModel, Task> hotkeyAction,
        Func<RealmCardViewModel, Task> cancelHotkeyAction,
        Func<RealmCardViewModel, Task> toggleRealmLockAction,
        Func<RealmCardViewModel, Task> setDefaultRealmAction)
    {
        DesktopId = snapshot.DesktopId;
        DesktopNumber = snapshot.DesktopNumber;
        DesktopName = string.IsNullOrWhiteSpace(snapshot.DesktopName) ? $"Desktop {snapshot.DesktopNumber}" : snapshot.DesktopName;
        IsCurrent = snapshot.IsCurrent;
        RealmPath = snapshot.RealmPath;
        IsNativeDesktopRealm = snapshot.IsNativeDesktopRealm;
        IsFavorite = snapshot.IsFavorite;
        _hotkey = snapshot.Hotkey;
        _wallpaperPath = snapshot.WallpaperPath;
        _wallpaperFileName = snapshot.WallpaperFileName;
        _wallpaperStatus = snapshot.WallpaperStatus;
        _hasWallpaperPreview = snapshot.HasWallpaperPreview;
        _wallpaperPreview = CreatePreview(snapshot.WallpaperPath);
        IsLayoutLocked = snapshot.IsLayoutLocked;
        IsRealmLocked = snapshot.IsRealmLocked;
        EffectiveLocked = snapshot.EffectiveLocked;
        HasSavedLayout = snapshot.HasSavedLayout;
        VariantCount = snapshot.VariantCount;
        SavedIconCount = snapshot.SavedIconCount;
        Variants = snapshot.Variants;
        _switchAction = switchAction;
        _editAction = editAction;
        _deleteAction = deleteAction;
        _wallpaperAction = wallpaperAction;
        _hotkeyAction = hotkeyAction;
        _cancelHotkeyAction = cancelHotkeyAction;
        _toggleRealmLockAction = toggleRealmLockAction;
        _setDefaultRealmAction = setDefaultRealmAction;

        SwitchCommand = new AsyncRelayCommand(() => _switchAction(this));
        EditCommand = new AsyncRelayCommand(() => _editAction(this));
        DeleteCommand = new AsyncRelayCommand(() => _deleteAction(this));
        WallpaperActionCommand = new AsyncRelayCommand(() => _wallpaperAction(this));
        HotkeyActionCommand = new AsyncRelayCommand(() => _hotkeyAction(this));
        ResetHotkeyCommand = new RelayCommand(ResetInlineHotkeyEdit);
        CancelHotkeyCommand = new AsyncRelayCommand(() => _cancelHotkeyAction(this));
        ToggleRealmLockCommand = new AsyncRelayCommand(() => _toggleRealmLockAction(this));
        SetDefaultRealmCommand = new AsyncRelayCommand(() => _setDefaultRealmAction(this));
    }

    public Guid DesktopId { get; }
    public int DesktopNumber { get; }
    public string DesktopName { get; }
    public bool IsCurrent { get; }
    public string RealmPath { get; }
    public bool IsNativeDesktopRealm { get; }
    public bool IsFavorite { get; }
    public bool IsLayoutLocked { get; }
    public bool IsRealmLocked { get; }
    public bool EffectiveLocked { get; }
    public bool HasSavedLayout { get; }
    public int VariantCount { get; }
    public int SavedIconCount { get; }
    public IReadOnlyList<IconLayoutVariantSnapshot> Variants { get; }

    public IAsyncRelayCommand SwitchCommand { get; }
    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand WallpaperActionCommand { get; }
    public IAsyncRelayCommand HotkeyActionCommand { get; }
    public IRelayCommand ResetHotkeyCommand { get; }
    public IAsyncRelayCommand CancelHotkeyCommand { get; }
    public IAsyncRelayCommand ToggleRealmLockCommand { get; }
    public IAsyncRelayCommand SetDefaultRealmCommand { get; }

    public string Title => DesktopName;
    public string DesktopDisplay => IsNativeDesktopRealm
        ? $"Native Windows Desktop folder · Virtual desktop #{DesktopNumber} · {DesktopId:B}"
        : $"Windows desktop #{DesktopNumber} · {DesktopId:B}";
    public string ActiveLabel => IsCurrent ? "CURRENT" : "READY";
    public Brush ActivePillBrush => (Brush)Application.Current.Resources[IsCurrent ? "DeskRealmAccentBrush" : "DeskRealmSurfaceRaisedBrush"];
    public Visibility DeleteVisibility => IsFavorite ? Visibility.Collapsed : Visibility.Visible;
    /// <summary>
    /// Raw persisted hotkey value used by the unified Realm Editor. It remains null when no hotkey is assigned; UI-only text is exposed separately through <see cref="HotkeyDisplay"/>.
    /// </summary>
    public string? Hotkey => _hotkey;
    public string HotkeyDisplay => string.IsNullOrWhiteSpace(_hotkey) ? "Not assigned" : _hotkey;
    public string IconCountDisplay => HasSavedLayout ? $"{SavedIconCount} icon{(SavedIconCount == 1 ? string.Empty : "s")}" : "No current layout snapshot";
    public string VariantDisplay => VariantCount == 0 ? "No saved variants" : $"{VariantCount} saved variant{(VariantCount == 1 ? string.Empty : "s")}";
    public string LockDisplay => IsRealmLocked
        ? "Realm locked"
        : IsLayoutLocked
            ? "Current layout locked"
            : EffectiveLocked
                ? "Variant locked"
                : "Unlocked";
    public string RealmPathDisplay => string.IsNullOrWhiteSpace(RealmPath)
        ? "This realm folder will be created when the realm is first activated."
        : IsNativeDesktopRealm
            ? $"Native Windows Desktop folder (preserved): {RealmPath}"
            : RealmPath;

    public string WallpaperPath => _wallpaperDraftPath ?? _wallpaperPath;
    public string WallpaperFileName => _wallpaperDraftPath is not null ? Path.GetFileName(_wallpaperDraftPath) : _wallpaperFileName;
    public string WallpaperStatus => _wallpaperDraftPath is not null
        ? "New wallpaper selected. Save to copy it into DeskRealm and apply it to this realm."
        : _wallpaperStatus;
    public ImageSource? WallpaperPreview => _wallpaperPreview;
    public bool HasWallpaperPreview => _wallpaperDraftPath is not null || _hasWallpaperPreview;
    public string WallpaperDisplay => string.IsNullOrWhiteSpace(WallpaperFileName)
        ? "No wallpaper assigned"
        : WallpaperFileName;
    public bool HasWallpaperDraft => !string.IsNullOrWhiteSpace(_wallpaperDraftPath);
    public string WallpaperActionGlyph => HasWallpaperDraft ? "💾" : "✏️";
    public string WallpaperActionTooltip => HasWallpaperDraft ? "Save wallpaper change" : "Choose a wallpaper for this realm";

    public bool IsHotkeyEditing => _isHotkeyEditing;
    public HotkeyCaptureField? HotkeyEditor => _hotkeyEditor;
    public Visibility HotkeyDisplayVisibility => _isHotkeyEditing ? Visibility.Collapsed : Visibility.Visible;
    public Visibility HotkeyEditorVisibility => _isHotkeyEditing ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HotkeyEditActionVisibility => _isHotkeyEditing ? Visibility.Collapsed : Visibility.Visible;
    public Visibility HotkeyEditorActionsVisibility => _isHotkeyEditing ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CancelHotkeyVisibility => _isHotkeyEditing ? Visibility.Visible : Visibility.Collapsed;
    public string HotkeyActionGlyph => "✏️";
    public string HotkeyActionTooltip => "Edit realm hotkey";

    // The glyph represents the current realm state, matching the adjacent status label.
    // The tooltip communicates the next explicit action so state and action can never be confused.
    public string RealmLockActionGlyph => IsRealmLocked ? "🔒" : "🔓";
    public string RealmLockActionTooltip => IsRealmLocked
        ? "Realm locked — click to unlock every layout in this realm"
        : "Realm unlocked — click to lock every layout in this realm";
    public string DefaultActionGlyph => IsFavorite ? "🌟" : "⭐";
    public string DefaultActionTooltip => IsFavorite ? "Current default realm" : "Set as default realm";

    public void SetWallpaperDraft(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new InvalidOperationException("Wallpaper draft path is empty.");
        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Wallpaper draft file was not found.", fullPath);
        var preview = CreatePreview(fullPath) ?? throw new InvalidOperationException("DeskRealm could not render a preview for the selected wallpaper file.");
        _wallpaperDraftPath = fullPath;
        _wallpaperPreview = preview;
        _hasWallpaperPreview = true;
        NotifyWallpaperChanged();
    }

    public string? WallpaperDraftPath => _wallpaperDraftPath;

    public void ClearWallpaperDraftAfterCommit()
    {
        _wallpaperDraftPath = null;
        NotifyWallpaperChanged();
    }

    public void BeginInlineHotkeyEdit()
    {
        if (_isHotkeyEditing) return;
        _hotkeyEditor = new HotkeyCaptureField(_hotkey);
        _isHotkeyEditing = true;
        OnPropertyChanged(nameof(IsHotkeyEditing));
        OnPropertyChanged(nameof(HotkeyEditor));
        OnPropertyChanged(nameof(HotkeyDisplayVisibility));
        OnPropertyChanged(nameof(HotkeyEditorVisibility));
        OnPropertyChanged(nameof(HotkeyEditActionVisibility));
        OnPropertyChanged(nameof(HotkeyEditorActionsVisibility));
        OnPropertyChanged(nameof(CancelHotkeyVisibility));
        OnPropertyChanged(nameof(HotkeyActionGlyph));
        OnPropertyChanged(nameof(HotkeyActionTooltip));
        _hotkeyEditor.StartCaptureWhenReady();
    }

    public string? CompleteInlineHotkeyEdit()
    {
        if (!_isHotkeyEditing) return _hotkey;
        var captured = _hotkeyEditor?.Value;
        _hotkey = captured;
        EndInlineHotkeyEdit();
        OnPropertyChanged(nameof(Hotkey));
        OnPropertyChanged(nameof(HotkeyDisplay));
        return captured;
    }

    public void CancelInlineHotkeyEdit() => EndInlineHotkeyEdit();

    public void ResetInlineHotkeyEdit()
    {
        _hotkeyEditor?.ResetToInitialValue();
    }

    private void EndInlineHotkeyEdit()
    {
        _isHotkeyEditing = false;
        _hotkeyEditor = null;
        OnPropertyChanged(nameof(IsHotkeyEditing));
        OnPropertyChanged(nameof(HotkeyEditor));
        OnPropertyChanged(nameof(HotkeyDisplayVisibility));
        OnPropertyChanged(nameof(HotkeyEditorVisibility));
        OnPropertyChanged(nameof(HotkeyEditActionVisibility));
        OnPropertyChanged(nameof(HotkeyEditorActionsVisibility));
        OnPropertyChanged(nameof(CancelHotkeyVisibility));
        OnPropertyChanged(nameof(HotkeyActionGlyph));
        OnPropertyChanged(nameof(HotkeyActionTooltip));
    }

    private void NotifyWallpaperChanged()
    {
        OnPropertyChanged(nameof(WallpaperPath));
        OnPropertyChanged(nameof(WallpaperFileName));
        OnPropertyChanged(nameof(WallpaperStatus));
        OnPropertyChanged(nameof(WallpaperPreview));
        OnPropertyChanged(nameof(HasWallpaperPreview));
        OnPropertyChanged(nameof(WallpaperDisplay));
        OnPropertyChanged(nameof(HasWallpaperDraft));
        OnPropertyChanged(nameof(WallpaperActionGlyph));
        OnPropertyChanged(nameof(WallpaperActionTooltip));
    }

    private static ImageSource? CreatePreview(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            return new BitmapImage(new Uri(Path.GetFullPath(path)));
        }
        catch
        {
            return null;
        }
    }
}
