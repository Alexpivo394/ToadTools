using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using ToadDesign;
using ToadTools.Desktop.Core;
using ToadTools.Desktop.Core.Plugin;
using ToadTools.Desktop.Core.Revit;
using ToadTools.Desktop.Core.Security;
using ToadTools.Desktop.Core.Settings;
using ToadTools.Desktop.Core.State;
using ToadTools.Desktop.Core.Updates;
using ToadTools.Desktop.Core.Windows;
using ToadTools.Desktop.Tray;
using ToadTools.Desktop.Views;

namespace ToadTools.Desktop;

/// <summary>
///     Программа ToadTools. Аргументы: без аргументов — трей и окно настроек; --background — только трей (автозапуск,
///     после установки); --settings — открыть настройки; --exit — закрыть запущенную копию; --uninstall — закрыть
///     запущенную копию и убрать плагин (вызывает установщик при удалении).
/// </summary>
public partial class App
{
    private const string MutexName = @"Local\ToadTools.Desktop";
    private const string ShowEventName = @"Local\ToadTools.Desktop.Show";
    private const string ExitEventName = @"Local\ToadTools.Desktop.Exit";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _exitEvent;
    private readonly CancellationTokenSource _lifetime = new();
    private Task _run = Task.CompletedTask;
    private bool _primary;

    public static AppPaths Paths => AppPaths.Default;
    public static Version Version { get; } = Assembly.GetExecutingAssembly().GetName().Version!;
    public static string ExePath => Process.GetCurrentProcess().MainModule!.FileName;

    public SettingsStore SettingsStore { get; } = new(Paths.SettingsFile);
    public UpdateSettings Settings { get; private set; } = new();
    public UpdateService Service { get; private set; } = null!;
    public TrayIconController Tray { get; private set; } = null!;
    public IReadOnlyList<string> LegacyInstalls { get; private set; } = [];

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Configure(Paths.LogsDir);
        var args = e.Args.Select(arg => arg.ToLowerInvariant()).ToArray();

        _mutex = new Mutex(true, MutexName, out var first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);

        if (args.Contains("--exit") || args.Contains("--uninstall"))
        {
            if (!first) CloseRunningCopy();
            if (args.Contains("--uninstall")) Uninstall();
            Shutdown(0);
            return;
        }

        if (!first)
        {
            _showEvent.Set();
            Shutdown(0);
            return;
        }

        _primary = true;
        Log.Info($"ToadTools {Version} запущен: {string.Join(" ", e.Args)}");
        DispatcherUnhandledException += (_, error) =>
        {
            Log.Error("Необработанная ошибка интерфейса", error.Exception);
            error.Handled = true;
        };

        ToadTheme.Mode = RememberedTheme();
        Settings = SettingsStore.Load();
        TryAutostart(Settings.StartWithWindows);
        LegacyInstalls = LegacyInstall.FindMachineWide();
        foreach (var legacy in LegacyInstalls) Log.Info($"Найдена старая установка для всех пользователей: {legacy}");

        Service = new UpdateService(
            Paths,
            () => Settings,
            new StateStore(Paths.StateFile),
            settings => settings.CreateHandler(),
            SignatureVerifier.Embedded(),
            new PluginDeployer(Paths),
            new RevitWatcher(),
            RevitDetector.InstalledYears,
            Version,
            Feed(e.Args));

        Tray = new TrayIconController(this);
        WaitForSignal(_showEvent, ShowSettings);
        WaitForSignal(_exitEvent, () => Shutdown(0));

        _run = Service.RunAsync(_lifetime.Token);
        if (!args.Contains("--background") || args.Contains("--settings")) ShowSettings();
    }

    /// <summary>Адрес релиза вместо GitHub: только Debug-сборка, для проверки обновления на своём сервере.</summary>
    private static Uri? Feed(string[] args)
    {
#if DEBUG
        var index = Array.FindIndex(args, arg => arg.Equals("--feed", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length && Uri.TryCreate(args[index + 1], UriKind.Absolute, out var feed)) return feed;
#endif
        return null;
    }

    /// <summary>Сохраняет настройки и применяет их: автозапуск, расписание, годы Revit.</summary>
    public void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Не удалось сохранить настройки", exception);
        }

        TryAutostart(Settings.StartWithWindows);
        Service.SettingsChanged();
    }

    public void ShowSettings()
    {
        var window = Windows.OfType<SettingsWindow>().FirstOrDefault() ?? new SettingsWindow(this);
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    /// <summary>Тихая установка новой версии программы: MSI заменит файлы и запустит программу снова.</summary>
    public void InstallAppUpdate()
    {
        if (Service.PendingAppMsi is not { } msi) return;
        AppUpdater.Install(msi, Paths.LogsDir);
        Shutdown(0);
    }

    private void CloseRunningCopy()
    {
        _exitEvent!.Set();
        try
        {
            if (!_mutex!.WaitOne(TimeSpan.FromSeconds(15))) Log.Error("Запущенная копия ToadTools не закрылась за 15 секунд");
        }
        catch (AbandonedMutexException)
        {
            // Копия завершилась, не отпустив мьютекс: это и нужно
        }
    }

    private static void Uninstall()
    {
        Log.Info("Удаление ToadTools: плагин убирается из Revit");
        try
        {
            Autostart.Set(false, "");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Error("Не удалось убрать автозапуск", exception);
        }

        Uninstaller.Run(Paths);
    }

    private static void TryAutostart(bool enabled)
    {
        try
        {
            Autostart.Set(enabled, ExePath);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("Не удалось настроить автозапуск", exception);
        }
    }

    private void WaitForSignal(WaitHandle handle, Action action) =>
        ThreadPool.RegisterWaitForSingleObject(handle, (_, _) => Dispatcher.BeginInvoke(action), null, Timeout.Infinite, false);

    /// <summary>Тема, выбранная в плагине (%APPDATA%\ToadTools\theme.json), иначе тема Windows.</summary>
    private static ToadThemeMode RememberedTheme()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", "theme.json");
            if (File.Exists(path) && System.Text.RegularExpressions.Regex.Match(File.ReadAllText(path), @"""dark""\s*:\s*(true|false)") is { Success: true } match)
                return match.Groups[1].Value == "true" ? ToadThemeMode.Dark : ToadThemeMode.Light;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return ToadThemeMode.System;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifetime.Cancel();
        Tray?.Dispose();
        Service?.Dispose();
        try
        {
            _run.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Мьютекс не наш (вторая копия): отпускать нечего
        }

        if (_primary) Log.Info("ToadTools завершён");
        base.OnExit(e);
    }
}
