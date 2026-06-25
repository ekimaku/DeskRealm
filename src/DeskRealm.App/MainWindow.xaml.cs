// DeskRealm-RealmStudio-Schema: v0.7.0
using DeskRealm.App.Interop;
using DeskRealm.App.Modals;
using DeskRealm.App.Services;
using DeskRealm.App.Shell;
using DeskRealm.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Windows.UI;

namespace DeskRealm.App;

public sealed partial class MainWindow : Window
{
    private readonly DeskRealmRuntime _runtime;
    private readonly Action _exitApplication;
    private readonly Func<Task> _restartApplication;
    private readonly MainViewModel _viewModel;
    private AppWindow _appWindow;
    private GlobalModalHost? _modalHost;
    private bool _allowApplicationExit;
    private bool _initialImportShown;
    private Guid? _inlineHotkeyCaptureDesktopId;
    private CancellationTokenSource? _runtimeRefreshDebounce;

    private static readonly Color TitleBarSurface = Color.FromArgb(255, 11, 32, 47);
    private static readonly Color TitleBarSurfaceInactive = Color.FromArgb(255, 8, 24, 35);
    private static readonly Color TitleBarText = Color.FromArgb(255, 231, 251, 255);
    private static readonly Color TitleBarMutedText = Color.FromArgb(255, 145, 184, 199);
    private static readonly Color TitleBarHover = Color.FromArgb(255, 18, 58, 73);
    private static readonly Color TitleBarPressed = Color.FromArgb(255, 43, 224, 220);

    internal MainWindow(DeskRealmRuntime runtime, Action exitApplication, Func<Task> restartApplication)
    {
        _runtime = runtime;
        _exitApplication = exitApplication;
        _restartApplication = restartApplication;
        InitializeComponent();

        WindowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(WindowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Title = "DeskRealm — Realm Studio";
        _appWindow.Resize(new Windows.Graphics.SizeInt32(1180, 790));
        _appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "DeskRealm.ico"));
        _appWindow.Closing += OnAppWindowClosing;
        ConfigureDeskRealmTitleBar();

        _viewModel = new MainViewModel(
            _runtime,
            SwitchRealmAsync,
            EditRealmAsync,
            DeleteRealmAsync,
            QuickWallpaperActionAsync,
            QuickHotkeyActionAsync,
            CancelQuickHotkeyEditAsync,
            ToggleRealmLockAsync,
            SetDefaultRealmAsync,
            CreateRealmAsync,
            async () => { await RunSafeAsync(() => _runtime.SaveIconLayoutAsync()); },
            async () => { await RunSafeAsync(() => _runtime.RestoreIconLayoutAsync()); },
            async () => { await RunSafeAsync(() => _runtime.SyncRealmNamesAsync()); },
            ApplyGlobalSettingsAsync,
            async () => { await RunSafeAsync(() => { _runtime.OpenPath(AppPaths.ConfigPath); return Task.CompletedTask; }); },
            async () => { await RunSafeAsync(() => { _runtime.OpenPath(AppPaths.LogDirectory); return Task.CompletedTask; }); },
            RestartDeskRealmAsync,
            QuitAsync);
        RootGrid.DataContext = _viewModel;

        _runtime.StateChanged += OnRuntimeStateChanged;
        _runtime.StrictOperationFailed += OnStrictOperationFailed;
    }


    private void ConfigureDeskRealmTitleBar()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            // The app remains usable with the platform title bar on unsupported systems.
            // This is an explicit visual compatibility boundary; no realm behavior changes.
            AppTitleBar.Visibility = Visibility.Collapsed;
            return;
        }

        ExtendsContentIntoTitleBar = true;
        _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var titleBar = _appWindow.TitleBar;
        titleBar.BackgroundColor = TitleBarSurface;
        titleBar.InactiveBackgroundColor = TitleBarSurfaceInactive;
        titleBar.ForegroundColor = TitleBarText;
        titleBar.InactiveForegroundColor = TitleBarMutedText;
        titleBar.ButtonBackgroundColor = TitleBarSurface;
        titleBar.ButtonInactiveBackgroundColor = TitleBarSurfaceInactive;
        titleBar.ButtonForegroundColor = TitleBarText;
        titleBar.ButtonInactiveForegroundColor = TitleBarMutedText;
        titleBar.ButtonHoverBackgroundColor = TitleBarHover;
        titleBar.ButtonHoverForegroundColor = TitleBarText;
        titleBar.ButtonPressedBackgroundColor = TitleBarPressed;
        titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 4, 37, 44);

        AppTitleBar.SizeChanged += (_, _) => UpdateTitleBarInsets();
        UpdateTitleBarInsets();
    }

    private void UpdateTitleBarInsets()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        var titleBar = _appWindow.TitleBar;
        AppTitleBarContent.Margin = new Thickness(titleBar.LeftInset + 14, 0, titleBar.RightInset + 12, 0);
    }

    public nint WindowHandle { get; }

    internal void InitializeLaunchState(DeskRealmLaunchState launch)
    {
        StatusText.Text = launch.RequiresInitialDesktopImport
            ? "First-run setup is waiting for an explicit Desktop association. Automatic switching remains paused until you choose."
            : launch.HotkeyRegistrationErrors.Count > 0
                ? "Realm engine is running, but one or more hotkeys need attention in Diagnostics."
                : launch.StartMinimized
                    ? "Realm engine is running in the notification area. Open this studio any time to inspect or change it."
                    : "Realm Studio is open. Closing the window keeps DeskRealm running in the notification area.";
        _ = RefreshSafelyAsync();
    }

    public void ShowFromTray()
    {
        NativeTrayIconService.ShowWindow(WindowHandle);
        Activate();
        _ = RefreshSafelyAsync();
    }

    public void HideToTray() => NativeTrayIconService.HideWindow(WindowHandle);

    public void AllowApplicationExit()
    {
        _allowApplicationExit = true;
        _runtimeRefreshDebounce?.Cancel();
        _runtime.StateChanged -= OnRuntimeStateChanged;
        _runtime.StrictOperationFailed -= OnStrictOperationFailed;
    }

    public async Task ShowInitialImportAsync()
    {
        if (_initialImportShown) return;
        _initialImportShown = true;
        try
        {
            var choice = await InitialDesktopImportModal.ShowAsync(ModalHost, _runtime.GetVirtualDesktops());
            if (choice is null)
            {
                StatusText.Text = "First-run setup is still waiting. DeskRealm will not switch automatically until you reopen the studio and choose explicitly.";
                return;
            }

            var completed = false;
            try
            {
                if (choice.Skip)
                {
                    await _runtime.SkipInitialImportAsync();
                }
                else
                {
                    await _runtime.CompleteInitialImportAsync(choice.DesktopId, choice.SaveLayout);
                }
                completed = true;
            }
            catch
            {
                // The runtime has already published the strict error through the global host.
            }
            if (completed)
            {
                StatusText.Text = "First-run setup is complete. DeskRealm automation is now enabled.";
            }
        }
        finally
        {
            await RefreshSafelyAsync();
        }
    }

    private GlobalModalHost ModalHost => _modalHost ??= new GlobalModalHost(RootGrid.XamlRoot ?? throw new InvalidOperationException("Realm Studio XAML root is not ready for modals."));

    private async Task SwitchRealmAsync(RealmCardViewModel realm)
    {
        if (await RunSafeAsync(() => _runtime.SwitchToRealmAsync(realm.DesktopId)))
        {
            StatusText.Text = $"Activation requested for {realm.DesktopName}.";
        }
    }

    private async Task QuickWallpaperActionAsync(RealmCardViewModel realm)
    {
        if (realm.HasWallpaperDraft)
        {
            var draft = realm.WallpaperDraftPath
                ?? throw new InvalidOperationException("Realm wallpaper draft was unexpectedly unavailable.");
            if (await RunSafeAsync(() => _runtime.SetRealmWallpaperAsync(realm.DesktopId, draft)))
            {
                realm.ClearWallpaperDraftAfterCommit();
                StatusText.Text = $"Wallpaper saved for {realm.DesktopName}.";
            }
            return;
        }

        try
        {
            var picker = new FileOpenPicker();
            foreach (var extension in WallpaperService.SupportedFileExtensions) picker.FileTypeFilter.Add(extension);
            InitializeWithWindow.Initialize(picker, (IntPtr)WindowHandle);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            realm.SetWallpaperDraft(file.Path);
            StatusText.Text = $"Wallpaper draft selected for {realm.DesktopName}. Save it from the preview bubble.";
        }
        catch (Exception ex)
        {
            await ModalHost.ShowErrorAsync("Wallpaper selection failed", ex.Message);
        }
    }

    private async Task QuickHotkeyActionAsync(RealmCardViewModel realm)
    {
        if (!realm.IsHotkeyEditing)
        {
            if (_inlineHotkeyCaptureDesktopId.HasValue && _inlineHotkeyCaptureDesktopId.Value != realm.DesktopId)
            {
                await ModalHost.ShowErrorAsync("Hotkey capture already active", "Finish or cancel the current inline hotkey capture before editing another realm.");
                return;
            }

            // Mark the capture before suspending global hotkeys. Suspend emits a runtime state
            // notification; RefreshSafelyAsync must see the active capture and keep this VCard
            // instance alive instead of rebuilding it between the first pencil click and render.
            _inlineHotkeyCaptureDesktopId = realm.DesktopId;
            if (!await RunSafeAsync(() => _runtime.SuspendGlobalHotkeysForCaptureAsync()))
            {
                _inlineHotkeyCaptureDesktopId = null;
                return;
            }
            realm.BeginInlineHotkeyEdit();
            StatusText.Text = $"Capturing a new hotkey for {realm.DesktopName}. Save or cancel this inline edit before switching cards.";
            return;
        }

        var capturedHotkey = realm.HotkeyEditor?.Value;
        if (!await RunSafeAsync(() => _runtime.UpdateRealmHotkeyAsync(realm.DesktopId, capturedHotkey))) return;

        realm.CompleteInlineHotkeyEdit();
        _inlineHotkeyCaptureDesktopId = null;
        await RunSafeAsync(() => _runtime.ResumeGlobalHotkeysAfterCaptureAsync());
        StatusText.Text = $"Hotkey saved for {realm.DesktopName}.";
        await RefreshSafelyAsync();
    }

    private async Task CancelQuickHotkeyEditAsync(RealmCardViewModel realm)
    {
        if (!realm.IsHotkeyEditing) return;
        realm.CancelInlineHotkeyEdit();
        _inlineHotkeyCaptureDesktopId = null;
        await RunSafeAsync(() => _runtime.ResumeGlobalHotkeysAfterCaptureAsync());
        StatusText.Text = $"Hotkey edit cancelled for {realm.DesktopName}.";
        await RefreshSafelyAsync();
    }

    private async Task ToggleRealmLockAsync(RealmCardViewModel realm)
    {
        if (await RunSafeAsync(() => _runtime.ToggleRealmLockAsync(realm.DesktopId)))
        {
            StatusText.Text = realm.IsRealmLocked
                ? $"Realm unlocked: {realm.DesktopName}. Individual variant locks were preserved."
                : $"Realm locked: {realm.DesktopName}. Every child variant is now protected by the realm lock.";
        }
    }

    private async Task SetDefaultRealmAsync(RealmCardViewModel realm)
    {
        if (realm.IsFavorite)
        {
            StatusText.Text = $"{realm.DesktopName} is already the default realm.";
            return;
        }

        if (await RunSafeAsync(() => _runtime.SetDefaultRealmAsync(realm.DesktopId)))
        {
            StatusText.Text = $"Default realm selected: {realm.DesktopName}.";
        }
    }

    private async Task RestartDeskRealmAsync()
    {
        var allowed = await ModalHost.ConfirmAsync(
            "Restart DeskRealm?",
            "DeskRealm will start a replacement process, close this process cleanly, and let the replacement wait until the single-instance owner is gone. Current desktop switching and hotkeys will be unavailable for a few seconds.",
            "Restart DeskRealm");
        if (!allowed) return;
        await _restartApplication();
    }

    private async Task CreateRealmAsync()
    {
        var allowed = await ModalHost.ConfirmAsync(
            "Add a Windows desktop?",
            "DeskRealm will send the Windows Win+Ctrl+D shortcut, wait for the new GUID, create its realm, then open the configuration modal. No desktop is created through an undocumented internal API.",
            "Add desktop");
        if (!allowed) return;

        VirtualDesktopInfo? created = null;
        if (await RunSafeAsync(async () => created = await _runtime.CreateVirtualDesktopAsync()) && created is not null)
        {
            StatusText.Text = $"New realm created: {created.Name}. Configure it now.";
            await RefreshSafelyAsync();
            var card = _viewModel.Realms.FirstOrDefault(item => item.DesktopId == created.Id);
            if (card is not null) await EditRealmAsync(card);
        }
    }

    private async Task DeleteRealmAsync(RealmCardViewModel realm)
    {
        var allowed = await ModalHost.ConfirmAsync(
            $"Delete the realm \"{realm.DesktopName}\"?",
            "DeskRealm will activate this desktop, then send Win+Ctrl+F4. Windows will move its windows to a remaining desktop. Deletion is refused for the final desktop, the default realm, or an active realm/layout/variant lock. The DeskRealm profile will be archived for explicit reuse later.",
            "Delete desktop");
        if (!allowed) return;

        if (await RunSafeAsync(() => _runtime.DeleteVirtualDesktopAsync(realm.DesktopId)))
        {
            StatusText.Text = $"Realm deleted: {realm.DesktopName}.";
        }
    }

    private async Task EditRealmAsync(RealmCardViewModel realm)
    {
        var captureScopeActive = false;
        try
        {
            // Registered global shortcuts can otherwise intercept the exact chord the user
            // is trying to capture. The scope is explicit and always restored in finally.
            if (!await RunSafeAsync(() => _runtime.SuspendGlobalHotkeysForCaptureAsync())) return;
            captureScopeActive = true;

            var request = await RealmEditorModal.ShowAsync(
                ModalHost,
                realm,
                WindowHandle,
                requestedName => _runtime.GetRealmNameAvailability(realm.DesktopId, requestedName));
            if (request is null) return;

            if (request.DeleteVariants.Count > 0)
            {
                var allowed = await ModalHost.ConfirmAsync(
                    "Delete marked variants?",
                    $"{request.DeleteVariants.Count} layout variant(s) will be deleted after saving. They can be recreated only by saving a layout again with the matching display topology.",
                    "Delete variants");
                if (!allowed) return;
            }

            if (!string.Equals(realm.DesktopName, request.DisplayName, StringComparison.Ordinal))
            {
                var nameAvailability = _runtime.GetRealmNameAvailability(realm.DesktopId, request.DisplayName);
                if (nameAvailability.HasActiveConflict && nameAvailability.ActiveConflict is not null)
                {
                    var conflict = nameAvailability.ActiveConflict;
                    await ModalHost.ShowErrorAsync(
                        "Realm name is already active",
                        $"Desktop #{conflict.Number} \"{conflict.Name}\" already owns this realm name. Archive choices only apply to deleted realms; DeskRealm will not merge two live Windows desktops.");
                    return;
                }

                if (!realm.IsNativeDesktopRealm && nameAvailability.HasArchivedRealm && request.DuplicateResolution == RealmDuplicateResolution.Ask)
                {
                    var resolution = await ArchivedRealmResolutionModal.ShowAsync(ModalHost, request.DisplayName, nameAvailability);
                    if (resolution is null) return;
                    request = request with { DuplicateResolution = resolution.Value };
                }

                var configuredMode = _runtime.Config.RealmRenameApplyMode;
                RealmRenameApplyChoice? choice;
                if (configuredMode == RealmRenameApplyMode.Ask)
                {
                    choice = await RealmRenameApplicationModal.ShowAsync(ModalHost, realm.DesktopName, request.DisplayName);
                    if (choice is null) return;
                }
                else
                {
                    choice = new RealmRenameApplyChoice(configuredMode, RememberChoice: false);
                }

                request = request with
                {
                    RenameApplyMode = choice.Mode,
                    RememberRenameApplyMode = choice.RememberChoice
                };
            }

            if (await RunSafeAsync(() => _runtime.SaveRealmStudioEditAsync(realm.DesktopId, request)))
            {
                var applicationSummary = string.Equals(realm.DesktopName, request.DisplayName, StringComparison.Ordinal)
                    ? "No Windows desktop-name change was requested."
                    : request.RenameApplyMode == RealmRenameApplyMode.RestartExplorer
                        ? "Windows Explorer was restarted to refresh Task View."
                        : "Task View will refresh the Windows desktop name on the next Explorer start or reboot.";
                StatusText.Text = $"Realm saved: {request.DisplayName}. {applicationSummary}";
            }
        }
        finally
        {
            if (captureScopeActive)
            {
                await RunSafeAsync(() => _runtime.ResumeGlobalHotkeysAfterCaptureAsync());
            }
        }
    }

    private async Task ApplyGlobalSettingsAsync()
    {
        if (await RunSafeAsync(() => _runtime.ApplyGlobalSettingsAsync(
            _viewModel.AutomationEnabled,
            _viewModel.StartWithWindows,
            _viewModel.StartMinimized,
            _viewModel.HotkeysEnabled,
            _viewModel.RememberRealmRenameApplyMode,
            _viewModel.RestartExplorerAfterRealmRename)))
        {
            StatusText.Text = "Automation settings saved.";
        }
    }

    private async Task QuitAsync()
    {
        if (!await ModalHost.ConfirmAsync("Quit DeskRealm?", "DeskRealm will stop automatic realm switching and restore the original Desktop when that safety option is enabled.", "Quit DeskRealm")) return;
        _exitApplication();
    }

    private async Task<bool> RunSafeAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch
        {
            // The runtime publishes the strict error through the global modal host.
            return false;
        }
        finally
        {
            await RefreshSafelyAsync();
        }
    }

    private async Task RefreshSafelyAsync()
    {
        if (_inlineHotkeyCaptureDesktopId.HasValue)
        {
            StatusText.Text = "Inline hotkey capture is active. Finish or cancel it before Realm Studio refreshes the cards.";
            return;
        }

        try
        {
            await _viewModel.RefreshAsync();
            StatusText.Text = _viewModel.StatusLine;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Realm Studio refresh failed: " + ex.Message;
        }
    }

    private void OnRuntimeStateChanged()
    {
        _runtimeRefreshDebounce?.Cancel();
        _runtimeRefreshDebounce?.Dispose();
        var cts = new CancellationTokenSource();
        _runtimeRefreshDebounce = cts;
        _ = DebounceRuntimeRefreshAsync(cts.Token);
    }

    private async Task DebounceRuntimeRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;
            DispatcherQueue.TryEnqueue(() => _ = RefreshSafelyAsync());
        }
        catch (OperationCanceledException)
        {
            // A newer Explorer/Registry event superseded this UI refresh request.
        }
    }

    private void OnStrictOperationFailed(string message) => _ = ModalHost.ShowErrorAsync("DeskRealm — strict operation stopped", message);

    private void MainNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        RealmsPage.Visibility = tag == "realms" ? Visibility.Visible : Visibility.Collapsed;
        AutomationPage.Visibility = tag == "automation" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = tag == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowApplicationExit) return;
        args.Cancel = true;
        if (_inlineHotkeyCaptureDesktopId.HasValue)
        {
            _ = CancelInlineHotkeyBeforeHideAsync();
            return;
        }

        HideToTray();
    }

    private async Task CancelInlineHotkeyBeforeHideAsync()
    {
        var activeId = _inlineHotkeyCaptureDesktopId;
        var card = activeId.HasValue ? _viewModel.Realms.FirstOrDefault(item => item.DesktopId == activeId.Value) : null;
        card?.CancelInlineHotkeyEdit();
        _inlineHotkeyCaptureDesktopId = null;
        await RunSafeAsync(() => _runtime.ResumeGlobalHotkeysAfterCaptureAsync());
        HideToTray();
    }
}
