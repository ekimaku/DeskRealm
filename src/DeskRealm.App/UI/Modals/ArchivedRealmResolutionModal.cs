using DeskRealm.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskRealm.App.Modals;

/// <summary>
/// Explicitly resolves an archived realm profile when the editor is in Ask mode.
/// This is intentionally separate from the editor so no archive action is ever
/// silently inferred from a matching display name.
/// </summary>
internal static class ArchivedRealmResolutionModal
{
    public static async Task<RealmDuplicateResolution?> ShowAsync(
        GlobalModalHost host,
        string requestedName,
        RealmNameAvailability availability)
    {
        if (!availability.HasArchivedRealm || availability.ArchivedProfile is null)
        {
            throw new InvalidOperationException("Archived realm resolution was requested without an archived realm profile.");
        }

        var archive = availability.ArchivedProfile;
        var archivedAt = archive.ArchivedAt == DateTimeOffset.MinValue
            ? "unknown time"
            : archive.ArchivedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(new TextBlock
        {
            Text = $"An archived DeskRealm profile named \"{requestedName}\" is available.",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        body.Children.Add(host.CreateSection(
            "ARCHIVE FOUND",
            archive.DesktopName,
            new TextBlock
            {
                Text = $"Archived {archivedAt}. Its stored layout and wallpaper can be explicitly reused for this new Windows desktop GUID.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource<Brush>("DeskRealmMutedBrush")
            },
            "This is an archive only. No live desktop shares the requested name."));

        body.Children.Add(host.CreateSection(
            "REUSE",
            "Reuse archived layout and wallpaper",
            new TextBlock
            {
                Text = "Copies the archived icon-layout source to this desktop GUID and restores the archived wallpaper assignment when its managed file still exists.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource<Brush>("DeskRealmMutedBrush")
            }));

        body.Children.Add(host.CreateSection(
            "START FRESH",
            "Replace archived layout with a fresh layout",
            new TextBlock
            {
                Text = "Keeps the requested realm name but starts this desktop with no reused archived layout. The old archive remains retained for audit and future explicit review.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource<Brush>("DeskRealmMutedBrush")
            }));

        var result = await host.ShowAsync(host.CreateStudioDialog(
            "ARCHIVED REALM",
            $"Reuse \"{requestedName}\"?",
            "Choose how this new desktop should relate to the retained archive before DeskRealm changes any folder or Windows desktop metadata.",
            body,
            primaryText: "Reuse archived layout",
            secondaryText: "Start fresh",
            closeText: "Cancel",
            minWidth: 620,
            preferredWidth: 700,
            maxWidth: 780,
            minHeight: 480,
            preferredHeight: 580,
            maxHeight: 700));

        return result switch
        {
            ContentDialogResult.Primary => RealmDuplicateResolution.ReuseArchivedLayout,
            ContentDialogResult.Secondary => RealmDuplicateResolution.OverwriteArchivedLayout,
            _ => null
        };
    }

    private static T Resource<T>(string key) where T : class
        => (Application.Current.Resources[key] as T) ?? throw new InvalidOperationException($"DeskRealm application resource '{key}' is missing.");
}
