namespace DeskRealm.App.Services;

internal static class AppPaths
{
    public static string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeskRealm");

    public static string LocalAppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeskRealm");

    public static string ConfigPath => Path.Combine(AppDataRoot, "deskrealm.config.json");

    public static string WallpapersRoot => Path.Combine(AppDataRoot, "wallpapers");

    public static string LogDirectory => Path.Combine(LocalAppDataRoot, "logs");

    public static string LogFilePath => Path.Combine(LogDirectory, "deskrealm.log");
}
