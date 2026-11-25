using System.ComponentModel;
using System.Diagnostics;
using ToadTools.Connection.ViewModels;

namespace ToadTools.Connection.Views;

/// <summary>Проводник и браузер Windows для окна «Настройки».</summary>
internal sealed class WindowsShell : ISettingsShell
{
    public void OpenFolder(string path) => Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void OpenUrl(string url) => Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public void ShowFile(string path) => Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    private static void Start(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info)?.Dispose();
        }
        catch (Win32Exception exception)
        {
            // Нет браузера или проводник не открылся: окно настроек продолжает работать.
            ToadTools.UI.Services.ToadLog.Error("Настройки", exception);
        }
    }
}
