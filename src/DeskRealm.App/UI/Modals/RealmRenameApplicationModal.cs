using DeskRealm.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskRealm.App.Modals;

/// <summary>
/// One explicit decision point for the Windows-shell consequence of changing a
/// Registry-backed virtual desktop name. This remains separate from RealmEditorModal
/// so the same policy can later be reused by any realm-rename entry point.
/// </summary>
internal static class RealmRenameApplicationModal
{
    public static async Task<RealmRenameApplyChoice?> ShowAsync(
        GlobalModalHost host,
        string currentName,
        string requestedName)
    {
        var remember = new CheckBox
        {
            Content = "Remember this choice in Automation",
            IsChecked = false
        };

        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(new TextBlock
        {
            Text = $"Windows virtual desktop: {currentName} → {requestedName}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        body.Children.Add(host.CreateSection(
            "WHAT WILL CHANGE",
            "Registry name is already the durable source",
            new TextBlock
            {
                Text = "DeskRealm writes the virtual desktop label to Windows Explorer metadata. Win + Tab reads that value when Explorer starts again.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource<Brush>("DeskRealmMutedBrush")
            }));

        body.Children.Add(host.CreateSection(
            "APPLY NOW",
            "Restart Windows Explorer",
            new TextBlock
            {
                Text = "This briefly closes File Explorer windows and hides the taskbar and Desktop while Explorer restarts. Your other applications keep running. Save or finish any File Explorer operation before continuing.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource<Brush>("DeskRealmMutedBrush")
            },
            "Choose “Apply and restart Explorer” to make the label appear in Win + Tab during this session."));

        body.Children.Add(host.CreateSection(
            "APPLY LATER",
            "Use the next reboot",
            new TextBlock
            {
                Text = "DeskRealm keeps the new name in Windows metadata and does not touch the running Explorer shell. Win + Tab will show the updated label after your next reboot or any manual Explorer restart.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource<Brush>("DeskRealmMutedBrush")
            }));

        body.Children.Add(remember);

        var result = await host.ShowAsync(host.CreateStudioDialog(
            "WINDOWS SHELL",
            "Apply the renamed virtual desktop",
            "Choose exactly when Task View should reload the new Windows desktop name.",
            body,
            primaryText: "Apply and restart Explorer",
            secondaryText: "Apply on next reboot",
            closeText: "Cancel",
            minWidth: 620,
            preferredWidth: 680,
            maxWidth: 760,
            minHeight: 470,
            preferredHeight: 560,
            maxHeight: 680));

        return result switch
        {
            ContentDialogResult.Primary => new RealmRenameApplyChoice(RealmRenameApplyMode.RestartExplorer, remember.IsChecked == true),
            ContentDialogResult.Secondary => new RealmRenameApplyChoice(RealmRenameApplyMode.NextReboot, remember.IsChecked == true),
            _ => null
        };
    }

    private static T Resource<T>(string key) where T : class
        => (Application.Current.Resources[key] as T) ?? throw new InvalidOperationException($"DeskRealm application resource '{key}' is missing.");
}
