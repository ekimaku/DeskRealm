using System.Diagnostics;
using System.Text.Json;

namespace DeskRealm.App.Services;

internal sealed class IconLayoutWorkerClientService : IDisposable
{
    private readonly FileLogger _logger;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private Process? _process;
    private bool _workerFailed;
    private bool _disposed;

    public IconLayoutWorkerClientService(FileLogger logger) => _logger = logger;

    public void Save(Guid virtualDesktopId, string realmName, int timeoutMs)
    {
        RunWorker(new IconWorkerRequest(Guid.NewGuid(), "save", virtualDesktopId, realmName, null, 0, 0), timeoutMs);
    }

    public void SaveCurrentVariant(Guid virtualDesktopId, string realmName, int timeoutMs)
    {
        RunWorker(new IconWorkerRequest(Guid.NewGuid(), "save-current-variant", virtualDesktopId, realmName, null, 0, 0), timeoutMs);
    }

    public void SaveIfChanged(Guid virtualDesktopId, string realmName, int timeoutMs)
    {
        RunWorker(new IconWorkerRequest(Guid.NewGuid(), "save-if-changed", virtualDesktopId, realmName, null, 0, 0), timeoutMs);
    }

    public void SaveLockedMergeNewIcons(Guid virtualDesktopId, string realmName, int timeoutMs)
    {
        RunWorker(new IconWorkerRequest(Guid.NewGuid(), "save-locked-merge-new-icons", virtualDesktopId, realmName, null, 0, 0), timeoutMs);
    }

    public void RestoreWhenReady(
        Guid virtualDesktopId,
        string realmName,
        string realmPath,
        int readinessTimeoutMs,
        int verificationTimeoutMs,
        int timeoutMs)
    {
        RunWorker(
            new IconWorkerRequest(
                Guid.NewGuid(),
                "restore-when-ready",
                virtualDesktopId,
                realmName,
                realmPath,
                readinessTimeoutMs,
                verificationTimeoutMs),
            timeoutMs);
    }

    private void RunWorker(IconWorkerRequest request, int timeoutMs)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_workerFailed)
            {
                throw new InvalidOperationException(
                    "The persistent icon worker failed earlier in this session. Restart DeskRealm after checking the log; no silent worker restart is attempted.");
            }

            var process = EnsureWorkerStarted();
            var requestJson = JsonSerializer.Serialize(request, _jsonOptions);
            var stopwatch = Stopwatch.StartNew();
            _logger.Info($"Icon worker command -> {request.Operation} {request.RealmName} {request.VirtualDesktopId:B}");

            try
            {
                process.StandardInput.WriteLine(requestJson);
                process.StandardInput.Flush();

                var readTask = process.StandardOutput.ReadLineAsync();
                if (!readTask.Wait(timeoutMs))
                {
                    KillWorkerAfterFailure();
                    throw new TimeoutException(
                        $"Persistent icon worker timeout after {timeoutMs} ms ({request.Operation} {request.RealmName}).");
                }

                var responseLine = readTask.Result;
                if (string.IsNullOrWhiteSpace(responseLine))
                {
                    KillWorkerAfterFailure();
                    throw new InvalidOperationException(
                        $"Persistent icon worker closed its response stream ({request.Operation} {request.RealmName}).");
                }

                IconWorkerProtocol.ValidateJsonLine(responseLine, "response");
                var response = JsonSerializer.Deserialize<IconWorkerResponse>(responseLine, _jsonOptions)
                    ?? throw new InvalidOperationException("Persistent icon worker returned an empty response.");

                if (response.Id != request.Id)
                {
                    KillWorkerAfterFailure();
                    throw new InvalidOperationException(
                        $"Persistent icon worker protocol mismatch. Expected {request.Id}, received {response.Id}.");
                }

                if (!response.Success)
                {
                    throw new InvalidOperationException(
                        $"Icon worker failed ({request.Operation} {request.RealmName}): {response.Error}");
                }

                stopwatch.Stop();
                _logger.Info(
                    $"[PERF] icon-worker command complete: operation={request.Operation}, " +
                    $"realm={request.RealmName}, elapsed={stopwatch.Elapsed.TotalMilliseconds:0.0} ms.");
            }
            catch
            {
                if (process.HasExited)
                {
                    _workerFailed = true;
                }

                throw;
            }
        }
    }

    private Process EnsureWorkerStarted()
    {
        if (_process is { HasExited: false })
        {
            return _process;
        }

        if (_process is not null || _workerFailed)
        {
            throw new InvalidOperationException(
                "The persistent icon worker is no longer available. Restart DeskRealm; no silent worker replacement is used.");
        }

        var exePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine DeskRealm.App.exe path to start the icon worker.");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = IconWorkerProtocol.Utf8NoBom,
            StandardOutputEncoding = IconWorkerProtocol.Utf8NoBom,
            StandardErrorEncoding = IconWorkerProtocol.Utf8NoBom
        };
        startInfo.ArgumentList.Add("--icon-layout-worker-server");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _logger.Warn("Icon worker stderr: " + e.Data);
            }
        };
        process.Exited += (_, _) =>
        {
            if (!_disposed)
            {
                _logger.Warn($"Persistent icon worker exited with code {process.ExitCode}.");
            }
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Cannot start the persistent DeskRealm icon worker.");
        }

        process.BeginErrorReadLine();
        _process = process;
        _logger.Info($"Persistent icon worker started: pid={process.Id}.");
        return process;
    }

    private void KillWorkerAfterFailure()
    {
        _workerFailed = true;
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(1000);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("Persistent icon worker kill failed: " + ex.Message);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_process is not null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        var shutdown = new IconWorkerRequest(Guid.NewGuid(), "shutdown", Guid.Empty, string.Empty, null, 0, 0);
                        _process.StandardInput.WriteLine(JsonSerializer.Serialize(shutdown, _jsonOptions));
                        _process.StandardInput.Flush();
                        if (!_process.WaitForExit(1000))
                        {
                            _logger.Warn("Persistent icon worker did not stop within the graceful shutdown guardrail; terminating it explicitly.");
                            _process.Kill(entireProcessTree: true);
                            _process.WaitForExit(1000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn("Persistent icon worker graceful shutdown failed: " + ex.Message);
                    KillWorkerAfterFailure();
                }
                finally
                {
                    _process.Dispose();
                    _process = null;
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

internal sealed record IconWorkerRequest(
    Guid Id,
    string Operation,
    Guid VirtualDesktopId,
    string RealmName,
    string? RealmPath,
    int ReadinessTimeoutMs,
    int VerificationTimeoutMs);

internal sealed record IconWorkerResponse(Guid Id, bool Success, string? Error);
