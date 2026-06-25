// DeskRealm-RealmStudio-Schema: v0.7.0
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskRealm.App.Modals;

/// <summary>
/// One serialized branded modal lane for the entire Realm Studio window.
/// Header and action footer remain fixed; only the body scrolls. Dialog geometry
/// is resolved from the live XamlRoot and belongs to the inner DeskRealm frame.
/// The outer ContentDialog only owns modal placement, so it never receives fixed
/// Width/MinWidth/MaxWidth values that can defeat WinUI centering or template sizing.
/// </summary>
internal sealed class GlobalModalHost
{
    private readonly XamlRoot _xamlRoot;
    private readonly SemaphoreSlim _lane = new(1, 1);

    public GlobalModalHost(XamlRoot xamlRoot) => _xamlRoot = xamlRoot;

    public async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await _lane.WaitAsync();
        try
        {
            dialog.XamlRoot = _xamlRoot;
            var platformResult = await dialog.ShowAsync();
            return dialog.Tag is DialogResultState state ? state.Result : platformResult;
        }
        finally
        {
            _lane.Release();
        }
    }

    public ContentDialog CreateStudioDialog(
        string eyebrow,
        string title,
        string subtitle,
        UIElement body,
        string? primaryText = null,
        string? secondaryText = null,
        string closeText = "Cancel",
        bool primaryIsDanger = false,
        double minWidth = 680,
        double? preferredWidth = null,
        double maxWidth = 980,
        double minHeight = 360,
        double? preferredHeight = null,
        double maxHeight = 760,
        Action<Button>? onPrimaryButtonCreated = null)
    {
        var geometry = ResolveGeometry(minWidth, preferredWidth ?? minWidth, maxWidth, minHeight, preferredHeight ?? maxHeight, maxHeight);
        ContentDialog? dialog = null;
        var resultState = new DialogResultState();
        var closeButton = new Button { Content = "×", Style = Resource<Style>("DeskRealmModalCloseButton") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(closeButton, "Close dialog");
        var cancelButton = new Button { Content = closeText, Style = Resource<Style>("DeskRealmSecondaryButton") };
        var secondaryButton = string.IsNullOrWhiteSpace(secondaryText) ? null : new Button { Content = secondaryText, Style = Resource<Style>("DeskRealmSecondaryButton") };
        var primaryButton = string.IsNullOrWhiteSpace(primaryText) ? null : new Button
        {
            Content = primaryText,
            Style = Resource<Style>(primaryIsDanger ? "DeskRealmDangerButton" : "DeskRealmPrimaryButton")
        };

        // DeskRealm-ContentDialog-TemplateWidthContract: exact dimensions belong
        // to this inner frame. The application-level ContentDialogMaxWidth resource
        // merely removes the stock template cap; it does not replace viewport clamping.
        var root = new Grid
        {
            Width = geometry.Width,
            Height = geometry.Height
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border
        {
            Background = Resource<Brush>("DeskRealmSurfaceRaisedBrush"),
            BorderBrush = Resource<Brush>("DeskRealmBorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(20, 20, 0, 0),
            Padding = new Thickness(24, 18, 16, 16)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerCopy = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 16, 0) };
        headerCopy.Children.Add(new TextBlock
        {
            Text = eyebrow.ToUpperInvariant(),
            Foreground = Resource<Brush>("DeskRealmAccentBrush"),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 70
        });
        headerCopy.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Resource<Brush>("DeskRealmTextBrush"),
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            headerCopy.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = Resource<Brush>("DeskRealmMutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Max(360, geometry.Width - 144)
            });
        }
        headerGrid.Children.Add(headerCopy);
        Grid.SetColumn(closeButton, 1);
        closeButton.VerticalAlignment = VerticalAlignment.Top;
        headerGrid.Children.Add(closeButton);
        header.Child = headerGrid;
        root.Children.Add(header);

        var scroller = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            Padding = new Thickness(24, 18, 16, 18)
        };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);

        var footer = new Border
        {
            Background = Resource<Brush>("DeskRealmSurfaceBrush"),
            BorderBrush = Resource<Brush>("DeskRealmBorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 20, 20),
            Padding = new Thickness(18, 14, 18, 16)
        };
        var footerActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 9 };
        footerActions.Children.Add(cancelButton);
        if (secondaryButton is not null) footerActions.Children.Add(secondaryButton);
        if (primaryButton is not null) footerActions.Children.Add(primaryButton);
        footer.Child = footerActions;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        // Keep outer ContentDialog placement fluid. Setting Width/MinWidth/MaxWidth
        // directly on ContentDialog is unreliable in WinUI and can stop recentering
        // when the parent window changes size; the inner frame above is authoritative.
        dialog = new ContentDialog
        {
            Style = Resource<Style>("DeskRealmModalDialog"),
            Content = new Border
            {
                Style = Resource<Style>("DeskRealmModalFrame"),
                Width = geometry.Width,
                Height = geometry.Height,
                Child = root
            },
            Tag = resultState
        };
        if (primaryButton is not null) onPrimaryButtonCreated?.Invoke(primaryButton);
        closeButton.Click += (_, _) => { resultState.Result = ContentDialogResult.None; dialog.Hide(); };
        cancelButton.Click += (_, _) => { resultState.Result = ContentDialogResult.None; dialog.Hide(); };
        if (secondaryButton is not null) secondaryButton.Click += (_, _) => { resultState.Result = ContentDialogResult.Secondary; dialog.Hide(); };
        if (primaryButton is not null) primaryButton.Click += (_, _) => { resultState.Result = ContentDialogResult.Primary; dialog.Hide(); };
        return dialog;
    }

    public Border CreateSection(string eyebrow, string title, UIElement body, string? description = null)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = eyebrow.ToUpperInvariant(),
            Foreground = Resource<Brush>("DeskRealmAccentBrush"),
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 65
        });
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 16 });
        if (!string.IsNullOrWhiteSpace(description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = Resource<Brush>("DeskRealmMutedBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }
        panel.Children.Add(body);
        return new Border { Style = Resource<Style>("DeskRealmModalSection"), Child = panel };
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Resource<Brush>("DeskRealmTextBrush") };
        await ShowAsync(CreateStudioDialog("DIAGNOSTIC", title, "DeskRealm stopped this operation before it could leave an ambiguous state.", body, closeText: "Close", minWidth: 560, maxWidth: 620, minHeight: 320, maxHeight: 520));
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Confirm")
    {
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Resource<Brush>("DeskRealmTextBrush") };
        var result = await ShowAsync(CreateStudioDialog("CONFIRMATION", title, "This action uses the same serialized DeskRealm operation lane as hotkeys and the tray.", body, confirmText, closeText: "Cancel", primaryIsDanger: confirmText.Contains("Delete", StringComparison.OrdinalIgnoreCase) || confirmText.Contains("Quit", StringComparison.OrdinalIgnoreCase), minWidth: 560, maxWidth: 680, minHeight: 320, maxHeight: 520));
        return result == ContentDialogResult.Primary;
    }

    private ModalGeometry ResolveGeometry(double minWidth, double preferredWidth, double maxWidth, double minHeight, double preferredHeight, double maxHeight)
    {
        const double horizontalMargin = 64;
        const double verticalMargin = 56;
        var availableWidth = Math.Max(360, _xamlRoot.Size.Width - horizontalMargin);
        var availableHeight = Math.Max(320, _xamlRoot.Size.Height - verticalMargin);
        var requestedWidth = Math.Clamp(preferredWidth, minWidth, maxWidth);
        var requestedHeight = Math.Clamp(preferredHeight, minHeight, maxHeight);
        var width = Math.Min(requestedWidth, availableWidth);
        var height = Math.Min(requestedHeight, availableHeight);
        return new ModalGeometry(width, height);
    }

    private sealed record ModalGeometry(double Width, double Height);

    private sealed class DialogResultState
    {
        public ContentDialogResult Result { get; set; } = ContentDialogResult.None;
    }

    private static T Resource<T>(string key) where T : class
        => (Application.Current.Resources[key] as T) ?? throw new InvalidOperationException($"DeskRealm application resource '{key}' is missing.");
}
