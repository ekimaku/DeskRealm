// DeskRealm-RealmStudio-Schema: v0.7.0
using DeskRealm.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskRealm.App.Modals;

internal sealed record InitialDesktopImportChoice(Guid DesktopId, bool SaveLayout, bool Skip);

internal static class InitialDesktopImportModal
{
    public static async Task<InitialDesktopImportChoice?> ShowAsync(GlobalModalHost host, IReadOnlyList<VirtualDesktopInfo> desktops)
    {
        if (desktops.Count == 0) throw new InvalidOperationException("Windows returned no virtual desktops for the initial DeskRealm setup.");

        var choices = desktops.OrderBy(desktop => desktop.Number).Select(desktop => new DesktopChoice(desktop)).ToList();
        var picker = new ComboBox
        {
            ItemsSource = choices,
            SelectedIndex = 0,
            DisplayMemberPath = nameof(DesktopChoice.Label),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var saveLayout = new ToggleSwitch { Header = "Save the current icon layout now", IsOn = true };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "DeskRealm needs one explicit first-run association. Choose the Windows virtual desktop that currently represents your existing Desktop. No files are moved by this setup step.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(picker);
        content.Children.Add(saveLayout);
        content.Children.Add(new TextBlock
        {
            Text = "You can also skip this import. DeskRealm will create safe realm folders instead, while leaving the original Desktop untouched.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        });

        var dialog = host.CreateStudioDialog(
            "FIRST-RUN SETUP",
            "Link the existing Desktop",
            "This step explicitly links your current Desktop to one Windows virtual desktop. No files are moved.",
            content,
            primaryText: "Link Desktop safely",
            secondaryText: "Skip import",
            closeText: "Not now",
            minWidth: 580,
            maxHeight: 620);
        var result = await host.ShowAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            return new InitialDesktopImportChoice(((DesktopChoice)picker.SelectedItem).Desktop.Id, saveLayout.IsOn, false);
        }
        if (result == ContentDialogResult.Secondary)
        {
            return new InitialDesktopImportChoice(Guid.Empty, false, true);
        }
        return null;
    }

    private sealed record DesktopChoice(VirtualDesktopInfo Desktop)
    {
        public string Label => $"Desktop #{Desktop.Number} — {Desktop.Name}";
    }
}
