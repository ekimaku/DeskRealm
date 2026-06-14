using System.Diagnostics;
using System.Text;

namespace DeskRealm.App.Services;

internal sealed class IconLayoutWorkerClientService
{
    private readonly FileLogger _logger;

    public IconLayoutWorkerClientService(FileLogger logger) => _logger = logger;

    public void Save(Guid virtualDesktopId, string realmName, int timeoutMs)
    {
        RunWorker("save", virtualDesktopId, realmName, timeoutMs);
    }

    public void SaveIfChanged(Guid virtualDesktopId, string realmName, int timeoutMs)
    {
        RunWorker("save-if-changed", virtualDesktopId, realmName, timeoutMs);
    }

    public void SaveLockedMergeNewIcons(Guid virtualDesktopId, string realmName, int timeoutMs)
    {
        RunWorker("save-locked-merge-new-icons", virtualDesktopId, realmName, timeoutMs);
    }

    public void Restore(Guid virtualDesktopId, string realmName, int timeoutMs)
    {
        RunWorker("restore", virtualDesktopId, realmName, timeoutMs);
    }

    private void RunWorker(string operation, Guid virtualDesktopId, string realmName, int timeoutMs)
    {
        var exePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine DeskRealm.App.exe path to start the icon worker.");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("--icon-layout-worker");
        startInfo.ArgumentList.Add(operation);
        startInfo.ArgumentList.Add(virtualDesktopId.ToString("B"));
        startInfo.ArgumentList.Add(realmName);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.Info($"Icon layout worker launch: {operation} {realmName} {virtualDesktopId:B}");

        if (!process.Start())
        {
            throw new InvalidOperationException("Cannot start the DeskRealm icon worker.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception killEx)
            {
                _logger.Warn($"Icon layout worker kill failed after timeout: {killEx.Message}");
            }

            throw new TimeoutException($"Icon worker timeout after {timeoutMs} ms ({operation} {realmName}).");
        }

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Icon worker failed with exit code {process.ExitCode} ({operation} {realmName})." + Environment.NewLine +
                $"STDOUT: {stdout}" + Environment.NewLine +
                $"STDERR: {stderr}");
        }

        _logger.Info($"Icon layout worker success: {operation} {realmName} {virtualDesktopId:B}");
    }
}
