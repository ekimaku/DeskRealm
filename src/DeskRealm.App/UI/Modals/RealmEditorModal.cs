// DeskRealm-RealmStudio-Schema: v0.7.0
using DeskRealm.App.Controls;
using DeskRealm.App.Services;
using DeskRealm.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DeskRealm.App.Modals;

/// <summary>
/// The unified Realm Studio editor: identity, native wallpaper, default behavior, hotkey,
/// realm/layout protection and topology variants are committed through one payload.
/// </summary>
internal static class RealmEditorModal
{
    private sealed record DuplicateChoice(string Label, RealmDuplicateResolution Value);

    public static async Task<RealmStudioEditRequest?> ShowAsync(
        GlobalModalHost host,
        RealmCardViewModel realm,
        nint ownerWindowHandle,
        Func<string, RealmNameAvailability> getNameAvailability)
    {
        var displayName = new TextBox
        {
            Text = realm.DesktopName,
            PlaceholderText = "e.g. Work, Gaming, Music",
            Style = Resource<Style>("DeskRealmTextInput")
        };
        var duplicateChoice = new ComboBox
        {
            ItemsSource = new[]
            {
                new DuplicateChoice("Ask before reusing an archived realm", RealmDuplicateResolution.Ask),
                new DuplicateChoice("Reuse archived layout and wallpaper", RealmDuplicateResolution.ReuseArchivedLayout),
                new DuplicateChoice("Replace archived layout with a fresh layout", RealmDuplicateResolution.OverwriteArchivedLayout)
            },
            DisplayMemberPath = nameof(DuplicateChoice.Label),
            SelectedIndex = 0
        };
        var nameResolutionStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Resource<Brush>("DeskRealmMutedBrush")
        };
        var archiveChoicePanel = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed
        };
        archiveChoicePanel.Children.Add(new TextBlock
        {
            Text = "An archived realm matches this name",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        archiveChoicePanel.Children.Add(duplicateChoice);
        RealmNameAvailability? currentNameAvailability = null;
        Button? saveButton = null;

        var defaultRealm = new ToggleButton
        {
            IsChecked = realm.IsFavorite,
            Content = realm.IsFavorite ? "★ Default realm at startup" : "☆ Set as default realm",
            Style = Resource<Style>("DeskRealmOutlineToggleButton"),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        defaultRealm.Checked += (_, _) => defaultRealm.Content = "★ Default realm at startup";
        defaultRealm.Unchecked += (_, _) => defaultRealm.Content = "☆ Set as default realm";

        var hotkey = new HotkeyCaptureField(realm.Hotkey);
        var wallpaperPreview = new WallpaperPreview(
            realm.WallpaperPath,
            string.IsNullOrWhiteSpace(realm.WallpaperPath) ? "REALM PREVIEW" : "CURRENT REALM WALLPAPER",
            realm.WallpaperDisplay,
            height: 190);
        var wallpaperPath = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(realm.WallpaperPath) ? "No DeskRealm wallpaper selected" : realm.WallpaperPath,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 54,
            Style = Resource<Style>("DeskRealmTextInput")
        };
        var wallpaperSourcePath = string.Empty;
        var clearWallpaper = false;
        var wallpaperStatus = new TextBlock
        {
            Text = "No changes are applied until you save.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Resource<Brush>("DeskRealmMutedBrush")
        };

        var realmLocked = new ToggleSwitch
        {
            Header = "Realm lock",
            OffContent = "Shared layouts can be changed",
            OnContent = "Protects every layout in this realm",
            IsOn = realm.IsRealmLocked
        };
        var layoutLocked = new ToggleSwitch
        {
            Header = "Current layout lock",
            OffContent = "This display topology can be changed",
            OnContent = "Protects the current display topology",
            IsOn = realm.IsLayoutLocked
        };

        var variantLocks = realm.Variants
            .Where(item => item.HasSavedLayout)
            .ToDictionary(item => item.DisplayTopologyKey, item => item.IsVariantLocked, StringComparer.OrdinalIgnoreCase);
        var deletedVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var variantRows = new List<VariantEditorRow>();
        var variantsPanel = new StackPanel { Spacing = 9 };
        if (realm.Variants.Any(item => item.HasSavedLayout))
        {
            foreach (var variant in realm.Variants.Where(item => item.HasSavedLayout).OrderByDescending(item => item.IsCurrentTopology).ThenByDescending(item => item.SavedAt))
            {
                var row = CreateVariantEditorRow(variant, variantLocks, deletedVariants);
                variantRows.Add(row);
                variantsPanel.Children.Add(row.Container);
            }
        }
        else
        {
            variantsPanel.Children.Add(new TextBlock
            {
                Text = "No saved layout variant yet. Activate this realm, then use Diagnostics → Save current layout to create the first variant.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource<Brush>("DeskRealmMutedBrush")
            });
        }

        void RefreshVariantInheritance()
        {
            foreach (var row in variantRows)
            {
                row.RefreshInheritance(realmLocked.IsOn, layoutLocked.IsOn);
            }
        }

        realmLocked.Toggled += (_, _) => RefreshVariantInheritance();
        layoutLocked.Toggled += (_, _) => RefreshVariantInheritance();
        RefreshVariantInheritance();

        var chooseWallpaper = new Button { Content = "Choose image…", Style = Resource<Style>("DeskRealmSecondaryButton") };
        chooseWallpaper.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker();
                foreach (var extension in WallpaperService.SupportedFileExtensions) picker.FileTypeFilter.Add(extension);
                InitializeWithWindow.Initialize(picker, (IntPtr)ownerWindowHandle);
                var file = await picker.PickSingleFileAsync();
                if (file is null) return;

                wallpaperSourcePath = file.Path;
                clearWallpaper = false;
                wallpaperPath.Text = file.Path;
                wallpaperPreview.SetSourcePath(file.Path, "NEW PREVIEW", Path.GetFileName(file.Path));
                wallpaperStatus.Text = "New image selected. DeskRealm copies and applies it only after you save.";
            }
            catch (Exception ex)
            {
                wallpaperStatus.Text = "Could not select an image: " + ex.Message;
            }
        };
        var clearWallpaperButton = new Button { Content = "Remove wallpaper", Style = Resource<Style>("DeskRealmSecondaryButton") };
        clearWallpaperButton.Click += (_, _) =>
        {
            wallpaperSourcePath = string.Empty;
            clearWallpaper = true;
            wallpaperPath.Text = "The DeskRealm wallpaper will be removed when you save.";
            wallpaperPreview.SetSourcePath(null, "NO WALLPAPER PREVIEW", "No DeskRealm wallpaper will be assigned after you save.");
            wallpaperStatus.Text = "The current wallpaper remains visible until you save.";
        };

        var body = new StackPanel { Spacing = 0 };
        body.Children.Add(CreateRealmSummary(realm));

        var identityBody = new StackPanel { Spacing = 9 };
        identityBody.Children.Add(new TextBlock { Text = "Realm name", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        identityBody.Children.Add(displayName);
        if (realm.IsNativeDesktopRealm)
        {
            identityBody.Children.Add(new TextBlock
            {
                Text = "This realm is linked to the native Windows Desktop folder. Saving or renaming it updates only the virtual desktop label and DeskRealm metadata; the native Desktop path and its files are never renamed, moved, or remapped.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource<Brush>("DeskRealmMutedBrush")
            });
        }
        else
        {
            identityBody.Children.Add(nameResolutionStatus);
            identityBody.Children.Add(archiveChoicePanel);
        }
        body.Children.Add(host.CreateSection(
            "IDENTITY",
            realm.IsNativeDesktopRealm ? "Native Desktop realm" : "Name and history",
            identityBody,
            realm.IsNativeDesktopRealm
                ? "The Windows desktop GUID remains immutable. The native Desktop folder is preserved as a Known Folder path."
                : "The Windows desktop GUID remains immutable. The name and managed realm folder can evolve without breaking layouts, wallpaper, or shortcuts."));

        var startupBody = new StackPanel { Spacing = 8 };
        startupBody.Children.Add(defaultRealm);
        startupBody.Children.Add(new TextBlock
        {
            Text = "Only one realm can be the default. DeskRealm activates it at startup only when global automation is enabled.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Resource<Brush>("DeskRealmMutedBrush")
        });
        body.Children.Add(host.CreateSection("STARTUP", "Default realm", startupBody));

        var hotkeyBody = new StackPanel { Spacing = 10 };
        hotkeyBody.Children.Add(new TextBlock
        {
            Text = "Global shortcuts are temporarily suspended while you edit, so the chord you capture never switches a realm.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Resource<Brush>("DeskRealmMutedBrush")
        });
        hotkeyBody.Children.Add(hotkey);
        body.Children.Add(host.CreateSection("HOTKEY", "Global capture", hotkeyBody, "The same HotkeyParser grammar is used for display, validation, and RegisterHotKey."));

        var wallpaperBody = new StackPanel { Spacing = 10 };
        wallpaperBody.Children.Add(wallpaperPreview);
        wallpaperBody.Children.Add(new TextBlock { Text = "Preview of the wallpaper associated with this realm GUID.", Foreground = Resource<Brush>("DeskRealmMutedBrush"), FontSize = 12 });
        wallpaperBody.Children.Add(wallpaperPath);
        var wallpaperActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        wallpaperActions.Children.Add(chooseWallpaper);
        wallpaperActions.Children.Add(clearWallpaperButton);
        wallpaperBody.Children.Add(wallpaperActions);
        wallpaperBody.Children.Add(wallpaperStatus);
        body.Children.Add(host.CreateSection("WALLPAPER", "Native Windows wallpaper per realm", wallpaperBody, "DeskRealm stores the image against the desktop GUID and applies it on the realm commit. No internal Virtual Desktop COM is used."));

        var protectionBody = new StackPanel { Spacing = 12 };
        protectionBody.Children.Add(realmLocked);
        protectionBody.Children.Add(layoutLocked);
        protectionBody.Children.Add(new TextBlock { Text = "Saved variants", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
        protectionBody.Children.Add(variantsPanel);
        body.Children.Add(host.CreateSection("LAYOUT PROTECTION", "Locks and variants", protectionBody, "Deletions are marked here only. A second confirmation is required before any mutation."));

        void RefreshNameAvailability()
        {
            var requestedName = displayName.Text.Trim();
            try
            {
                currentNameAvailability = getNameAvailability(requestedName);
                if (currentNameAvailability.HasActiveConflict && currentNameAvailability.ActiveConflict is not null)
                {
                    var conflict = currentNameAvailability.ActiveConflict;
                    nameResolutionStatus.Text = $"Live conflict: Desktop #{conflict.Number} \"{conflict.Name}\" already owns this realm name. Archive options cannot resolve a live desktop conflict.";
                    archiveChoicePanel.Visibility = Visibility.Collapsed;
                    if (saveButton is not null) saveButton.IsEnabled = false;
                    return;
                }

                if (currentNameAvailability.IsUnchanged)
                {
                    nameResolutionStatus.Text = "Current name. Saving other settings will not ask Windows Explorer to reload a desktop name.";
                    archiveChoicePanel.Visibility = Visibility.Collapsed;
                }
                else if (!realm.IsNativeDesktopRealm && currentNameAvailability.HasArchivedRealm && currentNameAvailability.ArchivedProfile is not null)
                {
                    var archive = currentNameAvailability.ArchivedProfile;
                    var timestamp = archive.ArchivedAt == DateTimeOffset.MinValue
                        ? "unknown time"
                        : archive.ArchivedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
                    nameResolutionStatus.Text = $"Archived realm found: \"{archive.DesktopName}\" from {timestamp}. Choose a policy below, or keep Ask to decide after Save.";
                    archiveChoicePanel.Visibility = Visibility.Visible;
                }
                else
                {
                    nameResolutionStatus.Text = realm.IsNativeDesktopRealm
                        ? "Available Windows desktop label. The native Desktop folder remains unchanged."
                        : "Available new realm name. No matching archived profile will be used.";
                    archiveChoicePanel.Visibility = Visibility.Collapsed;
                }

                if (saveButton is not null) saveButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                currentNameAvailability = null;
                nameResolutionStatus.Text = "Name unavailable: " + ex.Message;
                archiveChoicePanel.Visibility = Visibility.Collapsed;
                if (saveButton is not null) saveButton.IsEnabled = false;
            }
        }

        displayName.TextChanged += (_, _) => RefreshNameAvailability();

        var dialog = host.CreateStudioDialog(
            "REALM STUDIO",
            $"Realm {realm.DesktopNumber} — {realm.DesktopName}",
            "Everything that defines this realm is gathered here: identity, startup, hotkey, wallpaper, and protection.",
            body,
            primaryText: "Save realm",
            closeText: "Cancel",
            minWidth: 760,
            preferredWidth: 900,
            maxWidth: 980,
            minHeight: 560,
            preferredHeight: 748,
            maxHeight: 840,
            onPrimaryButtonCreated: button =>
            {
                saveButton = button;
                RefreshNameAvailability();
            });
        RefreshNameAvailability();
        var result = await host.ShowAsync(dialog);
        if (result != ContentDialogResult.Primary) return null;

        var selectedDuplicate = currentNameAvailability is { HasArchivedRealm: true } && !realm.IsNativeDesktopRealm
            ? duplicateChoice.SelectedItem as DuplicateChoice
            : null;
        return new RealmStudioEditRequest(
            displayName.Text.Trim(),
            defaultRealm.IsChecked == true,
            hotkey.Value,
            wallpaperSourcePath,
            clearWallpaper,
            realmLocked.IsOn,
            layoutLocked.IsOn,
            variantLocks,
            deletedVariants.ToArray(),
            selectedDuplicate?.Value ?? RealmDuplicateResolution.Ask);
    }

    private static Border CreateRealmSummary(RealmCardViewModel realm)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 3 };
        text.Children.Add(new TextBlock { Text = realm.DesktopDisplay, Foreground = Resource<Brush>("DeskRealmTextBrush"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = $"{realm.IconCountDisplay} · {realm.VariantDisplay}", Foreground = Resource<Brush>("DeskRealmMutedBrush"), FontSize = 12 });
        grid.Children.Add(text);
        var badge = new Border { Style = Resource<Style>("DeskRealmPill"), Background = realm.IsCurrent ? Resource<Brush>("DeskRealmAccentBrush") : Resource<Brush>("DeskRealmSurfaceRaisedBrush"), VerticalAlignment = VerticalAlignment.Top };
        badge.Child = new TextBlock { Text = realm.IsCurrent ? "CURRENT" : realm.IsNativeDesktopRealm ? "NATIVE DESKTOP" : "REALM", Foreground = Resource<Brush>("DeskRealmTextBrush"), FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);
        return new Border { Style = Resource<Style>("DeskRealmModalSection"), Padding = new Thickness(14), Child = grid };
    }

    private static VariantEditorRow CreateVariantEditorRow(IconLayoutVariantSnapshot variant, IDictionary<string, bool> variantLocks, ISet<string> deletedVariants)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel { Spacing = 3 };
        copy.Children.Add(new TextBlock { Text = variant.Summary, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        copy.Children.Add(new TextBlock
        {
            Text = $"{variant.IconCount} icon(s) · {(variant.SavedAt is null ? "no timestamp" : variant.SavedAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm"))}{(variant.IsCurrentTopology ? " · CURRENT" : string.Empty)}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Resource<Brush>("DeskRealmMutedBrush"),
            FontSize = 12
        });
        row.Children.Add(copy);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        var lockButton = new ToggleButton
        {
            IsChecked = variant.IsVariantLocked,
            Content = variant.IsVariantLocked ? "Locked" : "Lock",
            Style = Resource<Style>("DeskRealmOutlineToggleButton")
        };
        var delete = new Button { Content = "Delete", Style = Resource<Style>("DeskRealmDangerButton") };
        var state = new VariantEditorRow(variant, lockButton, delete, new Border { Style = Resource<Style>("DeskRealmCard"), Padding = new Thickness(12), Margin = new Thickness(0), Child = row });

        lockButton.Checked += (_, _) =>
        {
            variantLocks[variant.DisplayTopologyKey] = true;
            state.RefreshInheritance(false, false);
        };
        lockButton.Unchecked += (_, _) =>
        {
            variantLocks[variant.DisplayTopologyKey] = false;
            state.RefreshInheritance(false, false);
        };
        delete.Click += (_, _) =>
        {
            deletedVariants.Add(variant.DisplayTopologyKey);
            variantLocks.Remove(variant.DisplayTopologyKey);
            state.MarkDeleted();
        };
        actions.Children.Add(lockButton);
        actions.Children.Add(delete);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        return state;
    }

    private sealed class VariantEditorRow
    {
        private readonly IconLayoutVariantSnapshot _variant;
        private readonly ToggleButton _lockButton;
        private readonly Button _deleteButton;
        private bool _deleted;

        public VariantEditorRow(IconLayoutVariantSnapshot variant, ToggleButton lockButton, Button deleteButton, Border container)
        {
            _variant = variant;
            _lockButton = lockButton;
            _deleteButton = deleteButton;
            Container = container;
        }

        public Border Container { get; }

        public void MarkDeleted()
        {
            _deleted = true;
            _lockButton.IsEnabled = false;
            _deleteButton.IsEnabled = false;
            _deleteButton.Content = "Marked";
        }

        public void RefreshInheritance(bool realmLocked, bool currentLayoutLocked)
        {
            if (_deleted) return;
            var inheritedByRealm = realmLocked;
            var inheritedByCurrentLayout = !inheritedByRealm && currentLayoutLocked && _variant.IsCurrentTopology;
            if (inheritedByRealm || inheritedByCurrentLayout)
            {
                _lockButton.IsEnabled = false;
                _deleteButton.IsEnabled = false;
                _lockButton.Content = inheritedByRealm ? "Locked by realm" : "Locked by current layout";
                _deleteButton.Content = "Protected";
                return;
            }

            _lockButton.IsEnabled = true;
            _deleteButton.IsEnabled = true;
            _lockButton.Content = _lockButton.IsChecked == true ? "Locked" : "Lock";
            _deleteButton.Content = "Delete";
        }
    }

    private static T Resource<T>(string key) where T : class
        => (Application.Current.Resources[key] as T) ?? throw new InvalidOperationException($"DeskRealm application resource '{key}' is missing.");
}
