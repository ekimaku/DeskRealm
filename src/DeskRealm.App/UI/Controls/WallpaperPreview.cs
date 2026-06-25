// DeskRealm-RealmStudio-Schema: v0.7.0
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DeskRealm.App.Controls;

/// <summary>
/// Reusable Realm Studio wallpaper preview. It owns only visual state: selecting,
/// importing and applying wallpaper remains a service operation committed through
/// the unified Realm editor payload.
/// </summary>
internal sealed class WallpaperPreview : UserControl
{
    private readonly Image _image;
    private readonly Border _placeholder;
    private readonly TextBlock _label;
    private readonly TextBlock _description;

    public WallpaperPreview(string? sourcePath, string label, string description, double height = 184)
    {
        Height = height;
        MinHeight = height;

        // Border is sealed in WinUI. The preview therefore composes a styled Border
        // inside this UserControl instead of inheriting from it.
        var frame = new Border
        {
            Height = height,
            Background = Resource<Brush>("DeskRealmSurfaceRaisedBrush"),
            BorderBrush = Resource<Brush>("DeskRealmBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };

        var surface = new Grid();
        _image = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        surface.Children.Add(_image);
        surface.Children.Add(new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(112, 3, 14, 24)),
            CornerRadius = new CornerRadius(11)
        });

        _placeholder = new Border
        {
            Background = Resource<Brush>("DeskRealmSurfaceRaisedBrush"),
            CornerRadius = new CornerRadius(11),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 5,
                Children =
                {
                    new TextBlock
                    {
                        Text = "PREVIEW",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = Resource<Brush>("DeskRealmAccentBrush"),
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        CharacterSpacing = 90,
                        FontSize = 11
                    },
                    new TextBlock
                    {
                        Text = "No wallpaper selected",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = Resource<Brush>("DeskRealmMutedBrush")
                    }
                }
            }
        };
        surface.Children.Add(_placeholder);

        var caption = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(14, 12, 14, 13),
            Spacing = 4
        };
        var pill = new Border
        {
            Style = Resource<Style>("DeskRealmPill"),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _label = new TextBlock
        {
            Foreground = Resource<Brush>("DeskRealmTextBrush"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 10
        };
        pill.Child = _label;
        caption.Children.Add(pill);
        _description = new TextBlock
        {
            Foreground = Resource<Brush>("DeskRealmTextBrush"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2
        };
        caption.Children.Add(_description);
        surface.Children.Add(caption);

        frame.Child = surface;
        Content = frame;
        SetSourcePath(sourcePath, label, description);
    }

    // DeskRealm-WallpaperPreview-UserControlContract: visual-only state updates; no wallpaper commit.
    public void SetSourcePath(string? sourcePath, string label, string description)
    {
        _label.Text = label;
        _description.Text = description;
        var source = TryLoad(sourcePath);
        _image.Source = source;
        _placeholder.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static ImageSource? TryLoad(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
        try
        {
            return new BitmapImage(new Uri(Path.GetFullPath(sourcePath)));
        }
        catch
        {
            return null;
        }
    }

    private static T Resource<T>(string key) where T : class
        => (Application.Current.Resources[key] as T)
           ?? throw new InvalidOperationException($"DeskRealm application resource '{key}' is missing.");
}
