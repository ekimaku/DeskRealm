// DeskRealm-RealmStudio-Schema: v0.7.0
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskRealm.App.Shell;
using DeskRealm.App.Services;
using System.Collections.ObjectModel;

namespace DeskRealm.App.ViewModels;

internal sealed class MainViewModel : ObservableObject
{
    private readonly DeskRealmRuntime _runtime;
    private readonly Func<RealmCardViewModel, Task> _switchAction;
    private readonly Func<RealmCardViewModel, Task> _editAction;
    private readonly Func<RealmCardViewModel, Task> _deleteAction;
    private readonly Func<RealmCardViewModel, Task> _wallpaperAction;
    private readonly Func<RealmCardViewModel, Task> _hotkeyAction;
    private readonly Func<RealmCardViewModel, Task> _cancelHotkeyAction;
    private readonly Func<RealmCardViewModel, Task> _toggleRealmLockAction;
    private readonly Func<RealmCardViewModel, Task> _setDefaultRealmAction;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string _statusLine = "Starting Realm Studio…";
    private string _diagnosticText = "Loading runtime diagnostics…";
    private bool _automationEnabled;
    private bool _startWithWindows;
    private bool _startMinimized = true;
    private bool _hotkeysEnabled;
    private bool _rememberRealmRenameApplyMode;
    private bool _restartExplorerAfterRealmRename = true;
    private bool _isBusy;

    public MainViewModel(
        DeskRealmRuntime runtime,
        Func<RealmCardViewModel, Task> switchAction,
        Func<RealmCardViewModel, Task> editAction,
        Func<RealmCardViewModel, Task> deleteAction,
        Func<RealmCardViewModel, Task> wallpaperAction,
        Func<RealmCardViewModel, Task> hotkeyAction,
        Func<RealmCardViewModel, Task> cancelHotkeyAction,
        Func<RealmCardViewModel, Task> toggleRealmLockAction,
        Func<RealmCardViewModel, Task> setDefaultRealmAction,
        Func<Task> addDesktopAction,
        Func<Task> saveLayoutAction,
        Func<Task> restoreLayoutAction,
        Func<Task> syncNamesAction,
        Func<Task> applySettingsAction,
        Func<Task> openConfigAction,
        Func<Task> openLogsAction,
        Func<Task> restartAction,
        Func<Task> quitAction)
    {
        _runtime = runtime;
        _switchAction = switchAction;
        _editAction = editAction;
        _deleteAction = deleteAction;
        _wallpaperAction = wallpaperAction;
        _hotkeyAction = hotkeyAction;
        _cancelHotkeyAction = cancelHotkeyAction;
        _toggleRealmLockAction = toggleRealmLockAction;
        _setDefaultRealmAction = setDefaultRealmAction;
        AddDesktopCommand = new AsyncRelayCommand(addDesktopAction);
        SaveLayoutCommand = new AsyncRelayCommand(saveLayoutAction);
        RestoreLayoutCommand = new AsyncRelayCommand(restoreLayoutAction);
        SyncNamesCommand = new AsyncRelayCommand(syncNamesAction);
        ApplySettingsCommand = new AsyncRelayCommand(applySettingsAction);
        OpenConfigCommand = new AsyncRelayCommand(openConfigAction);
        OpenLogsCommand = new AsyncRelayCommand(openLogsAction);
        RestartCommand = new AsyncRelayCommand(restartAction);
        QuitCommand = new AsyncRelayCommand(quitAction);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public ObservableCollection<RealmCardViewModel> Realms { get; } = [];
    public IAsyncRelayCommand AddDesktopCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SaveLayoutCommand { get; }
    public IAsyncRelayCommand RestoreLayoutCommand { get; }
    public IAsyncRelayCommand SyncNamesCommand { get; }
    public IAsyncRelayCommand ApplySettingsCommand { get; }
    public IAsyncRelayCommand OpenConfigCommand { get; }
    public IAsyncRelayCommand OpenLogsCommand { get; }
    public IAsyncRelayCommand RestartCommand { get; }
    public IAsyncRelayCommand QuitCommand { get; }

    public string StatusLine { get => _statusLine; private set => SetProperty(ref _statusLine, value); }
    public string DiagnosticText { get => _diagnosticText; private set => SetProperty(ref _diagnosticText, value); }
    public bool AutomationEnabled { get => _automationEnabled; set => SetProperty(ref _automationEnabled, value); }
    public bool StartWithWindows { get => _startWithWindows; set => SetProperty(ref _startWithWindows, value); }
    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (!SetProperty(ref _startMinimized, value)) return;
            OnPropertyChanged(nameof(StartMinimizedDescription));
        }
    }
    public string StartMinimizedDescription => StartMinimized
        ? "DeskRealm launches directly to the notification area. First-run setup always remains visible."
        : "DeskRealm opens Realm Studio after launch. The notification-area icon remains available.";
    public bool HotkeysEnabled { get => _hotkeysEnabled; set => SetProperty(ref _hotkeysEnabled, value); }

    public bool RememberRealmRenameApplyMode
    {
        get => _rememberRealmRenameApplyMode;
        set
        {
            if (!SetProperty(ref _rememberRealmRenameApplyMode, value)) return;
            OnPropertyChanged(nameof(RealmRenameApplyDescription));
        }
    }

    public bool RestartExplorerAfterRealmRename
    {
        get => _restartExplorerAfterRealmRename;
        set
        {
            if (!SetProperty(ref _restartExplorerAfterRealmRename, value)) return;
            OnPropertyChanged(nameof(RealmRenameApplyDescription));
        }
    }

    public string RealmRenameApplyDescription => !RememberRealmRenameApplyMode
        ? "DeskRealm will ask when a Realm Studio save changes a Windows virtual-desktop label."
        : RestartExplorerAfterRealmRename
            ? "Remembered behavior: apply the Registry name and restart Windows Explorer immediately."
            : "Remembered behavior: apply the Registry name at the next Explorer start or reboot.";

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public async Task RefreshAsync()
    {
        if (!await _refreshGate.WaitAsync(0)) return;
        try
        {
            IsBusy = true;
            var snapshot = await _runtime.GetRealmStudioSnapshotAsync();
            Realms.Clear();
            foreach (var realm in snapshot.Realms)
            {
                Realms.Add(new RealmCardViewModel(
                    realm,
                    _switchAction,
                    _editAction,
                    _deleteAction,
                    _wallpaperAction,
                    _hotkeyAction,
                    _cancelHotkeyAction,
                    _toggleRealmLockAction,
                    _setDefaultRealmAction));
            }

            AutomationEnabled = snapshot.AutomationEnabled;
            StartWithWindows = snapshot.StartWithWindows;
            StartMinimized = snapshot.StartMinimized;
            HotkeysEnabled = snapshot.HotkeysEnabled;
            RememberRealmRenameApplyMode = snapshot.RealmRenameApplyPolicy != RealmRenameApplyMode.Ask;
            RestartExplorerAfterRealmRename = snapshot.RealmRenameApplyPolicy != RealmRenameApplyMode.NextReboot;
            StatusLine = snapshot.Status.LastMessage;
            DiagnosticText = BuildDiagnostics(snapshot);
        }
        finally
        {
            IsBusy = false;
            _refreshGate.Release();
        }
    }

    private static string BuildDiagnostics(RealmStudioSnapshot snapshot)
    {
        var status = snapshot.Status;
        var lastSwitch = status.LastSwitchAt == DateTimeOffset.MinValue ? "No completed switch yet" : status.LastSwitchAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        return string.Join(Environment.NewLine,
            $"Automation: {(snapshot.AutomationEnabled ? "active" : "paused")}",
            $"Windows startup registration: {(snapshot.WindowsStartupRegistered ? "present" : "not registered")}",
            $"Startup visibility: {(snapshot.StartMinimized ? "notification area" : "Realm Studio window")}",
            $"Hotkeys: {(snapshot.HotkeysEnabled ? "enabled" : "disabled")}",
            $"Realm rename apply policy: {snapshot.RealmRenameApplyPolicy}",
            $"Current desktop: {status.CurrentDesktopName}",
            $"Desktop GUID: {status.CurrentDesktopGuid}",
            $"Current realm folder: {status.CurrentRealmPath}",
            $"Known-folder Desktop: {status.KnownFolderDesktopPath}",
            $"Icon-layout worker: {snapshot.IconLayoutRuntimeStatus}",
            $"Last completed switch: {lastSwitch}",
            $"Assignments:{Environment.NewLine}{status.Assignments}");
    }
}
