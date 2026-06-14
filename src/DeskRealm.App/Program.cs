using DeskRealm.App.Services;
using DeskRealm.App.UI;

namespace DeskRealm.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--icon-layout-worker", StringComparison.OrdinalIgnoreCase))
        {
            RunIconLayoutWorker(args);
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
            var navigator = new VirtualDesktopNavigatorService(keyboard, logger);
            var switchService = new DesktopSwitchService(configService, knownFolder, virtualDesktop, shellRefresh, iconLayouts, navigator, logger);
            var hotkeys = new GlobalHotkeyService(logger);
            var startup = new StartupService(logger);

            Application.Run(new TrayAppContext(switchService, configService, hotkeys, startup, logger));
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

    private static void RunIconLayoutWorker(string[] args)
    {
        var logger = new FileLogger(AppPaths.LogFilePath);

        try
        {
            if (args.Length != 4)
            {
                throw new InvalidOperationException("Usage worker invalide : --icon-layout-worker <save|save-if-changed|save-locked-merge-new-icons|restore> <virtualDesktopGuid> <realmName>");
            }

            var operation = args[1];
            var virtualDesktopId = Guid.Parse(args[2]);
            var realmName = args[3];

            logger.Info($"Icon layout worker starting: {operation} {realmName} {virtualDesktopId:B}");

            var desktopIcons = new DesktopIconShellService(logger);
            var iconLayouts = new IconLayoutPersistenceService(desktopIcons, logger);

            if (string.Equals(operation, "save", StringComparison.OrdinalIgnoreCase))
            {
                iconLayouts.Save(virtualDesktopId, realmName);
            }
            else if (string.Equals(operation, "save-if-changed", StringComparison.OrdinalIgnoreCase))
            {
                iconLayouts.SaveIfChanged(virtualDesktopId, realmName);
            }
            else if (string.Equals(operation, "save-locked-merge-new-icons", StringComparison.OrdinalIgnoreCase))
            {
                iconLayouts.SaveLockedMergeNewIcons(virtualDesktopId, realmName);
            }
            else if (string.Equals(operation, "restore", StringComparison.OrdinalIgnoreCase))
            {
                iconLayouts.Restore(virtualDesktopId, realmName);
            }
            else
            {
                throw new InvalidOperationException($"Unknown worker operation: {operation}");
            }

            logger.Info($"Icon layout worker completed: {operation} {realmName} {virtualDesktopId:B}");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            logger.Error("Icon layout worker failed", ex);
            try
            {
                Console.Error.WriteLine(ex);
            }
            catch
            {
                // WinExe can have no attached console; logging above is authoritative.
            }

            Environment.Exit(31);
        }
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
