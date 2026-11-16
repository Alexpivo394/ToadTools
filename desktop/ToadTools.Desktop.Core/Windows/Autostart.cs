using Microsoft.Win32;

namespace ToadTools.Desktop.Core.Windows;

/// <summary>Запуск вместе с Windows: значение ToadTools в HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "ToadTools";

    public static void Set(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(Name, $"\"{exePath}\" --background");
        else if (key.GetValue(Name) != null) key.DeleteValue(Name);
    }

    public static bool IsSet()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) != null;
    }
}
