using Microsoft.Win32;

namespace ToadTools.Desktop.Core.Revit;

/// <summary>Установленные версии Revit: ключи HKLM\SOFTWARE\Autodesk\Revit\Autodesk Revit {год}.</summary>
public static class RevitDetector
{
    public const int FirstYear = 2020;
    public const int LastYear = 2030;

    public static bool IsInstalled(int year)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey($@"SOFTWARE\Autodesk\Revit\Autodesk Revit {year}");
            return key != null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static IReadOnlyList<int> InstalledYears() =>
        Enumerable.Range(FirstYear, LastYear - FirstYear + 1).Where(IsInstalled).ToArray();
}
