// DeskRealm-RealmStudio-Schema: v0.7.0
using DeskRealm.App.Interop;
using DeskRealm.App.Services;
using DeskRealm.App.Shell;
using Microsoft.UI.Xaml;
using System.Text.Json;

namespace DeskRealm.App;

public partial class App : Application
{
    private IDisposable? _singleInstance;
    private FileLogger? _logger;
    private DeskRealmRuntime? _runtime;
    private MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => LogUnhandled("Microsoft.UI.Xaml.Application.UnhandledException", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogUnhandled("AppDomain.UnhandledException", args.ExceptionObject as Exception ?? new InvalidOperationException(args.ExceptionObject?.ToString() ?? "Unknown fatal exception."));
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogUnhandled("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLineArguments = Environment.GetCommandLineArgs();
        if (commandLineArguments.Any(argument => string.Equals(argument, "--icon-layout-worker-server", StringComparison.OrdinalIgnoreCase)))
        {
            RunIconLayoutWorkerServer();
            Exit();
            return;
        }

        try
        {
            RestartDeskRealmService.WaitForRestartParentIfRequested(commandLineArguments);
        }
        catch (Exception ex)
        {
            NativeMessageBox.Show(ex.Message, "DeskRealm — restart failed", NativeMessageBox.Icon.Error);
            Exit();
            return;
        }

        _singleInstance = SingleInstanceGuard.Acquire("DeskRealm.App.SingleInstance.v0.1");
        if (_singleInstance is null)
        {
            NativeMessageBox.Show("DeskRealm is already running. Check the Windows notification area.", "DeskRealm", NativeMessageBox.Icon.Information);
            Exit();
            return;
        }

        _logger = new FileLogger(AppPaths.LogFilePath);
        _logger.Info("DeskRealm WinUI Realm Studio starting.");

        try
        {
            _runtime = DeskRealmRuntime.Create(_logger, action => _mainWindow?.DispatcherQueue.TryEnqueue(() => action()));
            _mainWindow = new MainWindow(_runtime, ExitDeskRealm, RestartDeskRealmAsync);
            _runtime.BindWindow(_mainWindow.WindowHandle, _mainWindow.ShowFromTray, ExitDeskRealm);

            var launch = _runtime.Start();
            _mainWindow.InitializeLaunchState(launch);
            if (launch.RequiresInitialDesktopImport)
            {
                // First-run setup is an explicit safety gate and must never be hidden by
                // the startup-visibility preference.
                _mainWindow.Activate();
                _ = _mainWindow.ShowInitialImportAsync();
            }
            else if (launch.StartMinimized)
            {
                _mainWindow.HideToTray();
            }
            else
            {
                _mainWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Fatal startup error", ex);
            NativeMessageBox.Show(ex.Message, "DeskRealm — fatal error", NativeMessageBox.Icon.Error);
            ExitDeskRealm();
        }
    }

    private Task RestartDeskRealmAsync()
    {
        try
        {
            RestartDeskRealmService.StartReplacementProcess();
            _logger?.Info("DeskRealm replacement process started by explicit in-app restart.");
            ExitDeskRealm();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogUnhandled("DeskRealm restart", ex);
            return Task.FromException(ex);
        }
    }

    private void ExitDeskRealm()
    {
        try
        {
            _runtime?.Dispose();
        }
        catch (Exception ex)
        {
            LogUnhandled("DeskRealm shutdown", ex);
            NativeMessageBox.Show(ex.Message, "DeskRealm — shutdown error", NativeMessageBox.Icon.Error);
        }
        finally
        {
            _singleInstance?.Dispose();
            _singleInstance = null;
            _logger?.Info("DeskRealm stopped.");
            _mainWindow?.AllowApplicationExit();
            Exit();
        }
    }

    private static void RunIconLayoutWorkerServer()
    {
        Console.InputEncoding = IconWorkerProtocol.Utf8NoBom;
        Console.OutputEncoding = IconWorkerProtocol.Utf8NoBom;
        var logger = new FileLogger(AppPaths.LogFilePath);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        try
        {
            var desktopIcons = new DesktopIconShellService(logger);
            var knownFolder = new KnownFolderService(logger);
            var iconLayouts = new IconLayoutPersistenceService(desktopIcons, knownFolder, logger);
            logger.Info("Persistent icon worker server ready.");
            string? line;
            while ((line = Console.ReadLine()) is not null)
            {
                IconWorkerRequest? request = null;
                IconWorkerResponse response;
                try
                {
                    IconWorkerProtocol.ValidateJsonLine(line, "request");
                    request = JsonSerializer.Deserialize<IconWorkerRequest>(line, jsonOptions)
                        ?? throw new InvalidOperationException("Icon worker request deserialized to null.");
                    if (string.Equals(request.Operation, "shutdown", StringComparison.OrdinalIgnoreCase))
                    {
                        response = new IconWorkerResponse(request.Id, true, null);
                        Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
                        Console.Out.Flush();
                        logger.Info("Persistent icon worker server received shutdown.");
                        return;
                    }

                    ExecuteIconWorkerRequest(iconLayouts, request);
                    response = new IconWorkerResponse(request.Id, true, null);
                }
                catch (ShellViewReadinessTimeoutException ex)
                {
                    logger.Warn("Persistent icon worker reached the bounded Explorer readiness timeout: " + ex.Message);
                    response = new IconWorkerResponse(
                        request?.Id ?? Guid.Empty,
                        false,
                        ex.ToString(),
                        IconWorkerFailureKinds.ShellReadinessTimeout);
                }
                catch (Exception ex)
                {
                    logger.Error("Persistent icon worker command failed", ex);
                    response = new IconWorkerResponse(
                        request?.Id ?? Guid.Empty,
                        false,
                        ex.ToString(),
                        IconWorkerFailureKinds.OperationFailure);
                }

                Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
                Console.Out.Flush();
            }
            logger.Warn("Persistent icon worker input stream closed.");
        }
        catch (Exception ex)
        {
            logger.Error("Persistent icon worker server failed", ex);
            try { Console.Error.WriteLine(ex); } catch { /* output can already be unavailable */ }
            Environment.Exit(32);
        }
    }

    private static void ExecuteIconWorkerRequest(IconLayoutPersistenceService iconLayouts, IconWorkerRequest request)
    {
        if (string.Equals(request.Operation, "save", StringComparison.OrdinalIgnoreCase)) { iconLayouts.Save(request.VirtualDesktopId, request.RealmName); return; }
        if (string.Equals(request.Operation, "save-current-variant", StringComparison.OrdinalIgnoreCase)) { iconLayouts.SaveCurrentVariant(request.VirtualDesktopId, request.RealmName); return; }
        if (string.Equals(request.Operation, "save-if-changed", StringComparison.OrdinalIgnoreCase)) { iconLayouts.SaveIfChanged(request.VirtualDesktopId, request.RealmName); return; }
        if (string.Equals(request.Operation, "save-locked-merge-new-icons", StringComparison.OrdinalIgnoreCase)) { iconLayouts.SaveLockedMergeNewIcons(request.VirtualDesktopId, request.RealmName); return; }
        if (string.Equals(request.Operation, "restore-when-ready", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.RealmPath)) throw new InvalidOperationException("restore-when-ready requires realmPath.");
            iconLayouts.RestoreWhenReady(request.VirtualDesktopId, request.RealmName, request.RealmPath, request.ReadinessTimeoutMs, request.VerificationTimeoutMs);
            return;
        }
        throw new InvalidOperationException($"Unknown persistent icon worker operation: {request.Operation}");
    }

    private static void LogUnhandled(string source, Exception exception)
    {
        try { new FileLogger(AppPaths.LogFilePath).Error(source, exception); } catch { /* avoid recursive crash */ }
    }
}
