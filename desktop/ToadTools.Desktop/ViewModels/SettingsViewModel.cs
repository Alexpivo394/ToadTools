using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToadTools.Desktop.Core;
using ToadTools.Desktop.Core.Revit;
using ToadTools.Desktop.Core.Settings;
using ToadTools.Desktop.Core.Updates;
using ToadTools.Desktop.Tray;

namespace ToadTools.Desktop.ViewModels;

/// <summary>Пункт выпадающего списка: значение и подпись.</summary>
public sealed record Choice<T>(T Value, string Title)
{
    public override string ToString() => Title;
}

/// <summary>Галочка «ставить плагин в Revit {год}».</summary>
public sealed partial class RevitYearOption(int year, bool enabled, Action changed) : ObservableObject
{
    public int Year { get; } = year;
    public string Title => $"Revit {Year}";

    [ObservableProperty] private bool _isEnabled = enabled;

    partial void OnIsEnabledChanged(bool value) => changed();
}

/// <summary>
///     Окно настроек. Изменения сохраняются сразу: каждое свойство пишет в <see cref="UpdateSettings" /> и вызывает
///     <see cref="App.SaveSettings" /> (файл, автозапуск, расписание проверок, годы Revit).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly App _app;
    private UpdateSettings Settings => _app.Settings;

    public SettingsViewModel(App app)
    {
        _app = app;
        Intervals =
        [
            new(CheckInterval.Manual, "Вручную"),
            new(CheckInterval.OnStartup, "При запуске Windows"),
            new(CheckInterval.Hours1, "Каждый час"),
            new(CheckInterval.Hours3, "Каждые 3 часа"),
            new(CheckInterval.Hours6, "Каждые 6 часов"),
            new(CheckInterval.Hours12, "Каждые 12 часов"),
            new(CheckInterval.Hours24, "Раз в день")
        ];
        Proxies =
        [
            new(ProxyMode.System, "Системный"),
            new(ProxyMode.None, "Без прокси"),
            new(ProxyMode.Custom, "Свой адрес")
        ];
        _interval = Intervals.First(choice => choice.Value == Settings.Interval);
        _proxy = Proxies.First(choice => choice.Value == Settings.Proxy);
        _proxyAddress = Settings.ProxyAddress ?? "";

        var installed = RevitDetector.InstalledYears();
        Years = new ObservableCollection<RevitYearOption>(installed.Select(year =>
            new RevitYearOption(year, Settings.IsYearEnabled(year), () => YearsChanged())));

        app.Service.Changed += OnServiceChanged;
        RefreshStatus();
    }

    public IReadOnlyList<Choice<CheckInterval>> Intervals { get; }
    public IReadOnlyList<Choice<ProxyMode>> Proxies { get; }
    public ObservableCollection<RevitYearOption> Years { get; }
    public bool HasYears => Years.Count > 0;
    public bool HasLegacyInstall => _app.LegacyInstalls.Count > 0;
    public string LegacyInstallText => string.Join("\n", _app.LegacyInstalls);

    public string AppVersion => $"Программа {App.Version.ToString(3)}";

    [ObservableProperty] private Choice<CheckInterval> _interval;
    [ObservableProperty] private Choice<ProxyMode> _proxy;
    [ObservableProperty] private string _proxyAddress;
    [ObservableProperty] private string _pluginVersion = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _lastCheck = "";
    [ObservableProperty] private string _releaseNotes = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _canInstallNow;
    [ObservableProperty] private bool _canUpdateApp;

    public bool IsCustomProxy => Proxy.Value == ProxyMode.Custom;

    public bool AutoDownload
    {
        get => Settings.AutoDownload;
        set => Change(() => Settings.AutoDownload = value);
    }

    public bool InstallWhenRevitCloses
    {
        get => Settings.InstallWhenRevitCloses;
        set => Change(() => Settings.InstallWhenRevitCloses = value);
    }

    public bool AutoUpdateApp
    {
        get => Settings.AutoUpdateApp;
        set => Change(() => Settings.AutoUpdateApp = value);
    }

    public bool StartWithWindows
    {
        get => Settings.StartWithWindows;
        set => Change(() => Settings.StartWithWindows = value);
    }

    public bool Notifications
    {
        get => Settings.Notifications;
        set => Change(() => Settings.Notifications = value);
    }

    partial void OnIntervalChanged(Choice<CheckInterval> value) => Change(() => Settings.Interval = value.Value);

    partial void OnProxyChanged(Choice<ProxyMode> value)
    {
        Change(() => Settings.Proxy = value.Value);
        OnPropertyChanged(nameof(IsCustomProxy));
    }

    partial void OnProxyAddressChanged(string value) => Change(() => Settings.ProxyAddress = value.Trim());

    private void YearsChanged() => Change(() =>
    {
        foreach (var year in Years) Settings.RevitYears[year.Year] = year.IsEnabled;
    });

    private void Change(Action apply)
    {
        apply();
        _app.SaveSettings();
    }

    [RelayCommand]
    private async Task CheckNow() => await _app.Service.CheckNowAsync(userInitiated: true);

    [RelayCommand]
    private async Task InstallNow()
    {
        if (!await _app.Service.InstallPluginNowAsync()) _app.Tray.Notify("Revit открыт", "Закройте Revit, чтобы установить новую версию.");
    }

    [RelayCommand]
    private void UpdateApp() => _app.InstallAppUpdate();

    [RelayCommand]
    private void OpenReleases() => TrayIconController.OpenUrl(TrayIconController.ReleasesUrl);

    [RelayCommand]
    private void OpenLogs()
    {
        Directory.CreateDirectory(App.Paths.LogsDir);
        Process.Start(new ProcessStartInfo(App.Paths.LogsDir) { UseShellExecute = true });
    }

    private void OnServiceChanged(object? sender, EventArgs e) => _app.Dispatcher.BeginInvoke(new Action(RefreshStatus));

    /// <summary>Окно закрыто: отписаться от сервиса.</summary>
    public void Detach() => _app.Service.Changed -= OnServiceChanged;

    private void RefreshStatus()
    {
        var service = _app.Service;
        PluginVersion = service.DeployedPlugin is { } version ? $"Плагин {version}" : "Плагин не установлен";
        StatusText = service.StatusText ?? "";
        IsBusy = service.Status is UpdateStatus.Checking or UpdateStatus.Downloading or UpdateStatus.Installing;
        IsError = service.Status == UpdateStatus.Error;
        CanInstallNow = service.Status == UpdateStatus.WaitingForRevit;
        CanUpdateApp = service.PendingApp != null;
        LastCheck = service.LastCheckUtc is { } last ? $"Последняя проверка: {last.ToLocalTime():dd.MM.yyyy HH:mm}" : "Ещё не проверялось";
        ReleaseNotes = CleanNotes(service.ReleaseNotes);
    }

    /// <summary>Описание релиза без markdown-заголовков и ссылок: короткий список для окна.</summary>
    private static string CleanNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";
        var lines = notes!.Replace("\r", "").Split('\n')
            .Select(line => System.Text.RegularExpressions.Regex.Replace(line, @"\[([^\]]+)\]\([^)]+\)", "$1").Replace("`", "").Replace("**", ""))
            .Where(line => !line.StartsWith("#") && line.Trim().Length > 0)
            .Select(line => line.TrimStart().StartsWith("- ") ? "•  " + line.TrimStart().Substring(2) : line.Trim())
            .Take(12);
        return string.Join("\n", lines);
    }
}
