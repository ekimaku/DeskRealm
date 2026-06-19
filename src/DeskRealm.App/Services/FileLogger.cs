using System.Security.Cryptography;
using System.Text;

namespace DeskRealm.App.Services;

internal sealed class FileLogger
{
    private const int CrossProcessLogGuardrailMs = 1500;

    private readonly string _path;
    private readonly object _sync = new();
    private readonly Mutex _crossProcessMutex;

    public FileLogger(string path)
    {
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var mutexHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(_path.ToUpperInvariant())));
        _crossProcessMutex = new Mutex(false, @"Local\DeskRealm.Log." + mutexHash);
    }

    public void Info(string message) => Write("INFO", message, null);
    public void Warn(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private void Write(string level, string message, Exception? ex)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
        var payload = ex is null
            ? line + Environment.NewLine
            : line + Environment.NewLine + ex + Environment.NewLine;

        lock (_sync)
        {
            var acquired = false;
            try
            {
                try
                {
                    acquired = _crossProcessMutex.WaitOne(CrossProcessLogGuardrailMs);
                }
                catch (AbandonedMutexException)
                {
                    // Ownership is transferred to this thread when the previous process ended abruptly.
                    acquired = true;
                }

                if (!acquired)
                {
                    throw new TimeoutException(
                        $"DeskRealm log mutex was not available within {CrossProcessLogGuardrailMs} ms: {_path}");
                }

                using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.Write(payload);
            }
            finally
            {
                if (acquired)
                {
                    _crossProcessMutex.ReleaseMutex();
                }
            }
        }
    }
}
