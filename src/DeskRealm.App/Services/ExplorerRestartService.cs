using System.Diagnostics;

namespace DeskRealm.App.Services;

/// <summary>
/// Explicitly restarts the Explorer shell for the current interactive session.
/// This is intentionally not a background refresh: callers must obtain user consent
/// before using it because taskbar/Desktop/File Explorer windows briefly disappear.
/// </summary>
internal sealed class ExplorerRestartService
{
    private const int ExplorerExitTimeoutMs = 8000;
    private const int ExplorerStartTimeoutMs = 8000;
    private const int PollIntervalMs = 100;

    private readonly FileLogger _logger;

    public ExplorerRestartService(FileLogger logger) => _logger = logger;

    public void RestartCurrentSessionExplorer()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var explorers = GetCurrentSessionExplorers(sessionId);
        if (explorers.Count == 0)
        {
            throw new InvalidOperationException(
                "Windows Explorer is not running in the current session. DeskRealm did not attempt a blind shell launch. " +
                "The desktop name was written to Registry and will be picked up at the next normal Explorer start or reboot.");
        }

        var processIds = explorers.Select(process => process.Id).ToArray();
        _logger.Warn(
            $"Explorer restart explicitly requested after virtual desktop rename: session={sessionId}, processes={string.Join(',', processIds)}. " +
            "Taskbar, Desktop and File Explorer windows will briefly disappear.");

        var terminationErrors = new List<string>();
        foreach (var explorer in explorers)
        {
            try
            {
                if (!explorer.HasExited)
                {
                    explorer.Kill(entireProcessTree: false);
                }
            }
            catch (Exception ex)
            {
                terminationErrors.Add($"PID {explorer.Id}: {ex.Message}");
            }
        }

        if (terminationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Explorer restart was stopped before a clean shell termination: " + string.Join(" | ", terminationErrors));
        }

        var deadline = Stopwatch.StartNew();
        foreach (var explorer in explorers)
        {
            try
            {
                var remaining = Math.Max(1, ExplorerExitTimeoutMs - (int)deadline.ElapsedMilliseconds);
                if (!explorer.WaitForExit(remaining))
                {
                    throw new InvalidOperationException($"Explorer PID {explorer.Id} did not exit within {ExplorerExitTimeoutMs} ms.");
                }
            }
            finally
            {
                explorer.Dispose();
            }
        }

        // Windows may already have relaunched the shell as part of its own session
        // recovery. Do not launch a second Explorer process when that verified state
        // is already present.
        var recoveredByWindows = GetCurrentSessionExplorers(sessionId);
        if (recoveredByWindows.Count > 0)
        {
            var recoveredIds = recoveredByWindows.Select(process => process.Id).ToArray();
            foreach (var process in recoveredByWindows) process.Dispose();
            _logger.Info(
                $"Explorer returned through Windows shell recovery after virtual desktop rename: session={sessionId}, processes={string.Join(',', recoveredIds)}.");
            return;
        }

        try
        {
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "explorer.exe"),
                UseShellExecute = true
            });
            started?.Dispose();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Explorer was terminated but could not be started again. Open Task Manager and run explorer.exe manually.", ex);
        }

        var startDeadline = Stopwatch.StartNew();
        while (startDeadline.ElapsedMilliseconds < ExplorerStartTimeoutMs)
        {
            var restarted = GetCurrentSessionExplorers(sessionId);
            if (restarted.Count > 0)
            {
                var restartedIds = restarted.Select(process => process.Id).ToArray();
                foreach (var process in restarted) process.Dispose();
                _logger.Info(
                    $"Explorer restart completed after virtual desktop rename: session={sessionId}, processes={string.Join(',', restartedIds)}, " +
                    $"elapsed={startDeadline.Elapsed.TotalMilliseconds:0} ms.");
                return;
            }

            Thread.Sleep(PollIntervalMs);
        }

        throw new InvalidOperationException(
            $"Explorer was terminated but did not return within {ExplorerStartTimeoutMs} ms. " +
            "Open Task Manager and run explorer.exe manually. The virtual desktop name remains written and will apply on the next successful Explorer start/reboot.");
    }

    private static List<Process> GetCurrentSessionExplorers(int sessionId)
    {
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                if (!process.HasExited && process.SessionId == sessionId)
                {
                    result.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
            catch
            {
                process.Dispose();
            }
        }

        return result;
    }
}
