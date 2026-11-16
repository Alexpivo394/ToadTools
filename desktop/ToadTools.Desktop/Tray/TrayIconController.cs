using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using ToadTools.Desktop.Core;
using ToadTools.Desktop.Core.Revit;
using ToadTools.Desktop.Core.Updates;
using ToadTools.Desktop.Views;

namespace ToadTools.Desktop.Tray;

/// <summary>
///     Значок в трее: подсказка со статусом, меню (проверить, установить сейчас, обновить программу, что нового,
///     настройки, выход) и уведомления Windows о новой версии и о законченной установке.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    public const string ReleasesUrl = "https://github.com/Alexpivo394/ToadTools/releases/latest";

    private readonly App _app;
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _status;
    private readonly MenuItem _installNow;
    private readonly MenuItem _updateApp;

    public TrayIconController(App app)
    {
        _app = app;
        _status = new MenuItem { IsEnabled = false };
        _installNow = new MenuItem { Header = "Установить сейчас" };
        _installNow.Click += async (_, _) =>
        {
            if (!await _app.Service.InstallPluginNowAsync()) Notify("Revit открыт", "Закройте Revit, чтобы установить новую версию.");
        };
        _updateApp = new MenuItem { Header = "Обновить программу" };
        _updateApp.Click += (_, _) => _app.InstallAppUpdate();

        var check = new MenuItem { Header = "Проверить обновления" };
        check.Click += async (_, _) => await _app.Service.CheckNowAsync(userInitiated: true);
        var notes = new MenuItem { Header = "Что нового" };
        notes.Click += (_, _) => OpenUrl(ReleasesUrl);
        var settings = new MenuItem { Header = "Настройки" };
        settings.Click += (_, _) => _app.ShowSettings();
        var exit = new MenuItem { Header = "Выход" };
        exit.Click += (_, _) => _app.Shutdown(0);

        var menu = new ContextMenu();
        foreach (var item in new object[] { _status, new Separator(), check, _installNow, _updateApp, notes, settings, new Separator(), exit })
            menu.Items.Add(item);
        menu.Opened += (_, _) => _installNow.IsEnabled = !new RevitWatcher().AnyRunning();

        _icon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/ToadTools;component/Resources/ToadTools.ico")),
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.RightClick,
            NoLeftClickDelay = true
        };
        _icon.TrayMouseDoubleClick += (_, _) => _app.ShowSettings();
        _icon.TrayLeftMouseUp += (_, _) => _app.ShowSettings();
        _icon.ForceCreate(false);

        var service = app.Service;
        service.Changed += (_, _) => OnUi(Refresh);
        service.PluginAvailable += (_, version) => OnUi(() =>
            Notify($"Доступна ToadTools {version}", "Откройте меню значка и нажмите «Проверить обновления», чтобы скачать."));
        service.PluginReady += (_, version) => OnUi(() => Notify($"Доступна ToadTools {version}",
            _app.Settings.InstallWhenRevitCloses
                ? "Закройте Revit — обновлю сам."
                : "Закройте Revit и нажмите «Установить сейчас» в меню значка."));
        service.PluginInstalled += (_, version) => OnUi(() =>
            Notify("ToadTools обновлён", $"Установлена версия {version}. Она загрузится при следующем запуске Revit."));
        service.AppUpdateReady += (_, version) => OnUi(() => OfferAppUpdate(version));

        if (app.LegacyInstalls.Count > 0)
            Notify("Найдена старая установка ToadTools", "Плагин установлен и «для всех пользователей». Удалите старую версию в «Программах и компонентах».");

        Refresh();
        if (service.PendingApp is { } pending) OnUi(() => OfferAppUpdate(pending));
    }

    private void Refresh()
    {
        var service = _app.Service;
        var plugin = service.DeployedPlugin?.ToString() ?? "не установлен";
        _status.Header = service.StatusText ?? $"ToadTools {plugin}";
        _installNow.Visibility = service.Status == UpdateStatus.WaitingForRevit ? Visibility.Visible : Visibility.Collapsed;
        _updateApp.Visibility = service.PendingApp != null ? Visibility.Visible : Visibility.Collapsed;
        _updateApp.Header = $"Обновить программу до {service.PendingApp}";
        var tip = $"ToadTools {plugin}" + (service.StatusText is { } text ? $"\n{text}" : "");
        _icon.ToolTipText = tip.Length > 120 ? tip.Substring(0, 120) : tip;
    }

    /// <summary>
    ///     Новая версия программы: ставится сама (если так в настройках, открытого окна настроек нет и плагин сейчас
    ///     не ставится), иначе — уведомление. Одну версию автоматически пробуем не чаще раза в сутки: если MSI падает,
    ///     программа не будет перезапускать его при каждом старте.
    /// </summary>
    private void OfferAppUpdate(Version version)
    {
        var busy = _app.Service.Status is UpdateStatus.Installing or UpdateStatus.Downloading or UpdateStatus.Checking
                   || _app.Windows.OfType<SettingsWindow>().Any(window => window.IsVisible);
#if DEBUG
        // Запуск из IDE не ставит поверх себя релизный MSI
        busy = true;
#endif
        if (_app.Settings.AutoUpdateApp && !busy && MayAttemptAutomatically(version))
        {
            _app.InstallAppUpdate();
            return;
        }

        Notify($"Новая версия программы ToadTools {version}", "Нажмите «Обновить программу» в меню значка.");
    }

    private static bool MayAttemptAutomatically(Version version)
    {
        var file = Path.Combine(App.Paths.LocalRoot, "app-update-attempt.txt");
        try
        {
            if (File.Exists(file))
            {
                var lines = File.ReadAllLines(file);
                if (lines.Length == 2 && lines[0] == version.ToString() && DateTime.TryParse(lines[1], out var when) &&
                    DateTime.UtcNow - when.ToUniversalTime() < TimeSpan.FromDays(1))
                {
                    Log.Info($"Программа {version} уже ставилась автоматически за последние сутки, жду кнопку");
                    return false;
                }
            }

            Directory.CreateDirectory(App.Paths.LocalRoot);
            File.WriteAllLines(file, [version.ToString(), DateTime.UtcNow.ToString("o")]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return true;
    }

    public void Notify(string title, string message)
    {
        if (!_app.Settings.Notifications) return;
        try
        {
            _icon.ShowNotification(title, message, NotificationIcon.Info);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error("Уведомление не показалось", exception);
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"Не открылся {url}", exception);
        }
    }

    private void OnUi(Action action) => _app.Dispatcher.BeginInvoke(action);

    public void Dispose() => _icon.Dispose();
}
