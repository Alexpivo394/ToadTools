using System.IO;

namespace ToadExporter.Core;

/// <summary>
///     Список серверов из RSN.ini — тот же, что Revit показывает в окне открытия с Revit Server.
/// </summary>
public static class RsnConfig
{
    public static string DefaultPath(string revitYear) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Autodesk", $"Revit Server {revitYear}", "Config", "RSN.ini");

    /// <summary>Имена серверов по строке на сервер; нет файла — пустой список.</summary>
    public static IReadOnlyList<string> ReadServers(string iniPath)
    {
        if (!File.Exists(iniPath)) return [];

        return File.ReadAllLines(iniPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
