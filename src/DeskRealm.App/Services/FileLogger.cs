using System.Text;

namespace DeskRealm.App.Services;

internal sealed class FileLogger
{
    private readonly string _path;
    private readonly object _sync = new();

    public FileLogger(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
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
            AppendWithRetry(payload);
        }
    }

    private void AppendWithRetry(string payload)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.Write(payload);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(25 * attempt);
            }
        }

        using var finalStream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var finalWriter = new StreamWriter(finalStream, Encoding.UTF8);
        finalWriter.Write(payload);
    }
}
