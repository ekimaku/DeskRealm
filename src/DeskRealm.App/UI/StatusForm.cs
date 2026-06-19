using DeskRealm.App.Services;

namespace DeskRealm.App.UI;

internal sealed class StatusForm : Form
{
    private readonly DesktopSwitchService _switchService;
    private readonly TextBox _textBox = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 10)
    };

    public StatusForm(DesktopSwitchService switchService)
    {
        _switchService = switchService;
        Text = "DeskRealm — status";
        Width = 820;
        Height = 520;
        Controls.Add(_textBox);
        Load += (_, _) => RefreshStatus();
    }

    public void RefreshStatus()
    {
        var status = _switchService.GetStatus();
        _textBox.Text =
            $"Enabled              : {status.Enabled}{Environment.NewLine}" +
            $"Current virtual desk : {status.CurrentDesktopName}{Environment.NewLine}" +
            $"Current GUID         : {status.CurrentDesktopGuid}{Environment.NewLine}" +
            $"Realm path           : {status.CurrentRealmPath}{Environment.NewLine}" +
            $"Known Desktop path   : {status.KnownFolderDesktopPath}{Environment.NewLine}" +
            $"Last switch          : {status.LastSwitchAt}{Environment.NewLine}" +
            $"Last message         : {status.LastMessage}{Environment.NewLine}" +
            $"Sync names           : {_switchService.Config.SyncRealmNamesWithVirtualDesktopNames}{Environment.NewLine}" +
            $"Icon layout persist  : {_switchService.Config.IconLayoutPersistenceEnabled}{Environment.NewLine}" +
            $"Shell ready timeout : {_switchService.Config.ShellViewReadyTimeoutMs} ms{Environment.NewLine}" +
            $"Icon verify timeout : {_switchService.Config.IconLayoutRestoreVerificationTimeoutMs} ms{Environment.NewLine}" +
            $"Icon runtime status : {_switchService.IconLayoutRuntimeStatus}{Environment.NewLine}" +
            $"Desktop hotkeys     : {_switchService.Config.DesktopHotkeysEnabled}{Environment.NewLine}" +
            $"Hotkey bindings     : {string.Join(", ", _switchService.Config.DesktopHotkeys.OrderBy(p => p.Key).Select(p => $"#{p.Key}={p.Value}"))}{Environment.NewLine}" +
            $"Start with Windows  : {_switchService.Config.StartWithWindows}{Environment.NewLine}" +
            $"{Environment.NewLine}" +
            $"Assignments          :{Environment.NewLine}{status.Assignments}{Environment.NewLine}" +
            $"{Environment.NewLine}" +
            $"Config               : {AppPaths.ConfigPath}{Environment.NewLine}" +
            $"Logs                 : {AppPaths.LogFilePath}{Environment.NewLine}" +
            $"Icon layouts         : {IconLayoutPersistenceService.LayoutRoot}{Environment.NewLine}";
    }
}
