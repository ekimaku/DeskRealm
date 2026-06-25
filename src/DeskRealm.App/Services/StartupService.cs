using Microsoft.Win32;
using System.Diagnostics;

namespace DeskRealm.App.Services;

internal sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DeskRealm";

    private readonly FileLogger _logger;

    public StartupService(FileLogger logger) => _logger = logger;

    public bool IsEnabledForCurrentExecutable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var exe = GetExecutablePath();
        return value.Contains(exe, StringComparison.OrdinalIgnoreCase);
    }

    public void Enable()
    {
        var exe = GetExecutablePath();
        var command = $"\"{exe}\"";
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException($"Cannot open/create HKCU\\{RunKeyPath}.");

        key.SetValue(ValueName, command, RegistryValueKind.String);
        _logger.Info($"Start with Windows enabled: {command}");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            _logger.Info($"Start with Windows already disabled: HKCU\\{RunKeyPath} absent.");
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
        _logger.Info("Start with Windows disabled.");
    }

    private static string GetExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        path = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        throw new InvalidOperationException("Cannot determine the DeskRealm executable path for Windows startup.");
    }
}
