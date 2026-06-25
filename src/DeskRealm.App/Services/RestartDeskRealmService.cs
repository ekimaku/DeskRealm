using System.Diagnostics;

namespace DeskRealm.App.Services;

/// <summary>
/// Starts a replacement DeskRealm process and lets the new process wait for the current
/// single-instance owner to exit before it acquires the mutex. This is explicit recovery,
/// not an in-process pseudo reload. The service supports both published EXEs and the
/// development `dotnet run` host so Diagnostics behaves consistently during local testing.
/// </summary>
internal static class RestartDeskRealmService
{
    private const string RestartAfterPidArgument = "--restart-after-pid";
    private const int ParentExitTimeoutMs = 15000;

    public static void StartReplacementProcess()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            throw new InvalidOperationException("DeskRealm could not determine its process path for a controlled restart.");
        }

        var assemblyPath = typeof(RestartDeskRealmService).Assembly.Location;
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
        {
            throw new InvalidOperationException("DeskRealm could not determine its managed application assembly for a controlled restart.");
        }

        var isDotnetHost = string.Equals(
            Path.GetFileNameWithoutExtension(processPath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);

        var fileName = isDotnetHost ? processPath : processPath;
        var arguments = isDotnetHost
            ? $"\"{assemblyPath}\" {RestartAfterPidArgument} {Environment.ProcessId}"
            : $"{RestartAfterPidArgument} {Environment.ProcessId}";

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(assemblyPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false
        });

        if (process is null)
        {
            throw new InvalidOperationException("DeskRealm could not start its replacement process.");
        }

        process.Dispose();
    }

    public static void WaitForRestartParentIfRequested(string[] commandLineArguments)
    {
        if (!TryGetParentProcessId(commandLineArguments, out var parentProcessId)) return;
        if (parentProcessId <= 0 || parentProcessId == Environment.ProcessId) return;

        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            if (!parent.HasExited && !parent.WaitForExit(ParentExitTimeoutMs))
            {
                throw new TimeoutException($"DeskRealm replacement process timed out waiting for PID {parentProcessId} to exit.");
            }
        }
        catch (ArgumentException)
        {
            // The old process is already gone, which is the desired restart state.
        }
    }

    private static bool TryGetParentProcessId(string[] arguments, out int processId)
    {
        processId = 0;
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (!string.Equals(arguments[index], RestartAfterPidArgument, StringComparison.OrdinalIgnoreCase)) continue;
            return int.TryParse(arguments[index + 1], out processId);
        }

        return false;
    }
}
