using System.Diagnostics;
using System.Windows.Interop;
using ToadTools.Connection.ViewModels;
using ToadTools.Connection.Views;
using ToadTools.UI.Services;

namespace ToadTools.Connection;

/// <summary>
///     Окно «Настройки» ToadTools: модальное, одно на Revit — повторный вызов, пока оно открыто, переключает раздел
///     и выводит окно вперёд.
/// </summary>
public static class SettingsDialog
{
    private static SettingsWindow? _open;

    /// <summary>
    ///     Год Revit для раздела «О ToadTools». Подключение не ссылается на Revit API — значение записывает приложение
    ///     ToadTools при запуске.
    /// </summary>
    public static string RevitVersion { get; set; } = "";

    /// <summary>Открывает окно на разделе; владелец — главное окно Revit.</summary>
    public static void Show(SettingsSection section) => Show(section, RevitWindow());

    /// <param name="section">Раздел, открытый первым.</param>
    /// <param name="owner">Окно-владелец: главное окно Revit или окно плагина.</param>
    /// <returns>
    ///     true — в этом окне выполнен вход в ToadFlow, и подключение сохранено: тот, кто открыл окно, перечитывает
    ///     данные. Уже открытое окно только выводится вперёд — false.
    /// </returns>
    public static bool Show(SettingsSection section, IntPtr owner)
    {
        if (_open is not null)
        {
            _open.ViewModel.Section = section;
            _open.Activate();
            return false;
        }

        var connection = new ConnectionViewModel(ToadFlowClient.Shared, ConnectionStore.Shared);
        var model = new SettingsViewModel(connection, ThemePreference.Instance, new WindowsShell(), SettingsPaths.Default, RevitVersion,
            ToadToolsVersion()) { Section = section };
        var window = new SettingsWindow(model);
        if (owner != IntPtr.Zero) _ = new WindowInteropHelper(window) { Owner = owner };
        _open = window;
        try
        {
            window.ShowDialog();
        }
        finally
        {
            _open = null;
        }

        return connection.SignedIn && ConnectionStore.Shared.Load() is not null;
    }

    /// <summary>Версия ToadTools: три числа версии сборки (все сборки решения собираются с одной версией).</summary>
    private static string ToadToolsVersion()
    {
        var version = typeof(SettingsDialog).Assembly.GetName().Version ?? new Version(0, 0, 0);
        return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    private static IntPtr RevitWindow()
    {
        using var process = Process.GetCurrentProcess();
        return process.MainWindowHandle;
    }
}
