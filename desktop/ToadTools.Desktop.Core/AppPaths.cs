namespace ToadTools.Desktop.Core;

/// <summary>
///     Папки программы. Локальные (программа, временные файлы, лог, состояние) — в %LOCALAPPDATA%\ToadTools,
///     настройки — в %APPDATA%\ToadTools, плагин — в папках Revit %APPDATA%\Autodesk\Revit\Addins\{год}.
/// </summary>
public sealed class AppPaths(string localRoot, string roamingRoot, string addinsRoot)
{
    public static AppPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins"));

    public string LocalRoot { get; } = localRoot;
    public string AppDir => Path.Combine(LocalRoot, "app");
    public string StagingDir => Path.Combine(LocalRoot, "staging");
    public string DownloadsDir => Path.Combine(LocalRoot, "downloads");
    public string LogsDir => Path.Combine(LocalRoot, "logs");
    public string StateFile => Path.Combine(LocalRoot, "state.json");
    public string SettingsFile => Path.Combine(roamingRoot, "settings.json");
    public string AddinsRoot { get; } = addinsRoot;

    /// <summary>Папка надстроек Revit одного года: Addins\2025.</summary>
    public string AddinsYear(int year) => Path.Combine(AddinsRoot, year.ToString());
}
