using DeskRealm.App.Services;
using DeskRealm.App.UI;
using System.Text.Json;

namespace DeskRealm.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--icon-layout-worker-server", StringComparison.OrdinalIgnoreCase))
        {
            RunIconLayoutWorkerServer();
            return;
        }



        ApplicationConfiguration.Initialize();

        Application.ThreadException += (_, e) => LogUnhandled("Application.ThreadException", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogUnhandled("AppDomain.UnhandledException", ex);
            }
            else
            {
                LogUnhandled("AppDomain.UnhandledException", new InvalidOperationException(e.ExceptionObject?.ToString() ?? "Exception inconnue"));
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogUnhandled("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        using var appLock = SingleInstanceGuard.Acquire("DeskRealm.App.SingleInstance.v0.1");
        if (appLock is null)
        {
            MessageBox.Show(
                "DeskRealm is already running. Check the Windows notification area.",
                "DeskRealm",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var logger = new FileLogger(AppPaths.LogFilePath);
        logger.Info("DeskRealm starting.");

        try
        {
            var configService = new RealmConfigService(logger);
            var knownFolder = new KnownFolderService(logger);
            var virtualDesktop = new VirtualDesktopRegistryService(logger);
            var shellRefresh = new ShellRefreshService(logger);
            var iconLayouts = new IconLayoutWorkerClientService(logger);
            var keyboard = new KeyboardInputService(logger);
            var navigator = new VirtualDesktopNavigatorService(keyboard, virtualDesktop, logger);
            var switchService = new DesktopSwitchService(configService, knownFolder, virtualDesktop, shellRefresh, iconLayouts, navigator, keyboard, logger);
            var hotkeys = new GlobalHotkeyService(logger);
            var startup = new StartupService(logger);
            var desktopChanges = new VirtualDesktopChangeMonitor(logger);

            Application.Run(new TrayAppContext(
                switchService,
                configService,
                hotkeys,
                startup,
                desktopChanges,
                iconLayouts,
                logger));
        }
        catch (Exception ex)
        {
            logger.Error("Fatal startup error", ex);
            MessageBox.Show(ex.Message, "DeskRealm — fatal error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            logger.Info("DeskRealm stopped.");
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
                catch (Exception ex)
                {
                    logger.Error("Persistent icon worker command failed", ex);
                    response = new IconWorkerResponse(
                        request?.Id ?? Guid.Empty,
                        false,
                        ex.ToString());
                }

                Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
                Console.Out.Flush();
            }

            logger.Warn("Persistent icon worker input stream closed.");
        }
        catch (Exception ex)
        {
            logger.Error("Persistent icon worker server failed", ex);
            try
            {
                Console.Error.WriteLine(ex);
            }
            catch
            {
                // Redirected stderr may already be unavailable.
            }

            Environment.Exit(32);
        }
    }

    private static void ExecuteIconWorkerRequest(
        IconLayoutPersistenceService iconLayouts,
        IconWorkerRequest request)
    {
        if (string.Equals(request.Operation, "save", StringComparison.OrdinalIgnoreCase))
        {
            iconLayouts.Save(request.VirtualDesktopId, request.RealmName);
            return;
        }

        if (string.Equals(request.Operation, "save-current-variant", StringComparison.OrdinalIgnoreCase))
        {
            iconLayouts.SaveCurrentVariant(request.VirtualDesktopId, request.RealmName);
            return;
        }

        if (string.Equals(request.Operation, "save-if-changed", StringComparison.OrdinalIgnoreCase))
        {
            iconLayouts.SaveIfChanged(request.VirtualDesktopId, request.RealmName);
            return;
        }

        if (string.Equals(request.Operation, "save-locked-merge-new-icons", StringComparison.OrdinalIgnoreCase))
        {
            iconLayouts.SaveLockedMergeNewIcons(request.VirtualDesktopId, request.RealmName);
            return;
        }

        if (string.Equals(request.Operation, "restore-when-ready", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.RealmPath))
            {
                throw new InvalidOperationException("restore-when-ready requires realmPath.");
            }

            iconLayouts.RestoreWhenReady(
                request.VirtualDesktopId,
                request.RealmName,
                request.RealmPath,
                request.ReadinessTimeoutMs,
                request.VerificationTimeoutMs);
            return;
        }

        throw new InvalidOperationException($"Unknown persistent icon worker operation: {request.Operation}");
    }

    private static void LogUnhandled(string source, Exception ex)
    {
        try
        {
            var logger = new FileLogger(AppPaths.LogFilePath);
            logger.Error(source, ex);
        }
        catch
        {
            // Avoid recursive crash while handling a crash.
        }
    }
}
