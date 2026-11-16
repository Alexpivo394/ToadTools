namespace ToadTools.Desktop.Core.Plugin;

/// <summary>
///     Удаление программы (ToadTools.exe --uninstall из установщика): плагин убирается из всех версий Revit, рабочие
///     папки и состояние удаляются. Настройки и логи остаются — на случай повторной установки.
/// </summary>
public static class Uninstaller
{
    public static void Run(AppPaths paths)
    {
        var deployer = new PluginDeployer(paths);
        deployer.Recover();
        if (Directory.Exists(paths.AddinsRoot))
        {
            foreach (var folder in Directory.GetDirectories(paths.AddinsRoot))
            {
                if (!int.TryParse(Path.GetFileName(folder), out var year) || !deployer.IsDeployed(year)) continue;
                try
                {
                    deployer.Remove(year);
                    Log.Info($"ToadTools убран из Revit {year}");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Log.Error($"Не удалось убрать ToadTools из Revit {year} (открыт Revit?)", exception);
                }
            }
        }

        foreach (var folder in new[] { paths.StagingDir, paths.DownloadsDir })
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Не удалось удалить {folder}", exception);
            }
        }

        if (File.Exists(paths.StateFile)) File.Delete(paths.StateFile);
    }
}

/// <summary>Старая установка плагина «для всех пользователей» (MultiUser MSI): программа её не трогает, только сообщает.</summary>
public static class LegacyInstall
{
    public static IReadOnlyList<string> MachineWideRoots() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "Revit", "Addins"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk", "Revit", "Addins")
    ];

    public static IReadOnlyList<string> FindMachineWide(IEnumerable<string>? roots = null) =>
        (roots ?? MachineWideRoots())
        .Where(Directory.Exists)
        .SelectMany(root => SafeDirectories(root).Select(year => Path.Combine(year, PluginArchive.AddinFile)))
        .Where(File.Exists)
        .ToArray();

    private static string[] SafeDirectories(string root)
    {
        try
        {
            return Directory.GetDirectories(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
