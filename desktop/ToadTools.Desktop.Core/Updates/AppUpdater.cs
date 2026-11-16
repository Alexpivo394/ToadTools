using System.Diagnostics;

namespace ToadTools.Desktop.Core.Updates;

/// <summary>
///     Обновление самой программы: тихая установка скачанного MSI. Программа сразу после запуска завершается, MSI
///     заменяет её файлы и в конце запускает снова (custom action установщика). Лог MSI — в папке логов программы.
/// </summary>
public static class AppUpdater
{
    public static ProcessStartInfo StartInfo(string msiPath, string logsDir) => new()
    {
        FileName = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
        Arguments = $"/i \"{msiPath}\" /qn /norestart /l*v \"{Path.Combine(logsDir, $"msi-{DateTime.Now:yyyy-MM-dd-HHmmss}.log")}\"",
        UseShellExecute = false,
        CreateNoWindow = true
    };

    public static void Install(string msiPath, string logsDir)
    {
        Directory.CreateDirectory(logsDir);
        Log.Info($"Обновление программы: {msiPath}");
        using var process = Process.Start(StartInfo(msiPath, logsDir));
    }
}
