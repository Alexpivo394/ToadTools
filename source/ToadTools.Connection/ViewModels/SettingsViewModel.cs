using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToadTools.UI.Services;

namespace ToadTools.Connection.ViewModels;

/// <summary>Окно «Настройки» ToadTools: ToadFlow, вид, журнал, кэш, «О ToadTools».</summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    public const string WikiUrl = "https://github.com/Alexpivo394/ToadTools/wiki";
    public const string ChangelogUrl = "https://github.com/Alexpivo394/ToadTools/blob/master/Changelog.md";

    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly IThemePreference _appearance;
    private readonly ISettingsShell _shell;
    private readonly SettingsPaths _paths;

    [ObservableProperty] private SettingsSection _section;

    /// <summary>Итог действий раздела «Журнал»: архив собран, журналы очищены или причина сбоя.</summary>
    [ObservableProperty] private string? _logStatus;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CreateArchiveCommand))]
    private bool _isArchiving;

    [ObservableProperty] private string _thumbnailsSize = "";

    public SettingsViewModel(ConnectionViewModel connection, IThemePreference appearance, ISettingsShell shell, SettingsPaths paths,
        string revitVersion, string toadToolsVersion)
    {
        Connection = connection;
        _appearance = appearance;
        _shell = shell;
        _paths = paths;
        RevitVersion = revitVersion;
        ToadToolsVersion = toadToolsVersion;
        _appearance.PropertyChanged += OnAppearanceChanged;
        ThumbnailsSize = FormatSize(FolderSize(paths.Thumbnails));
    }

    /// <summary>Раздел ToadFlow: вход, кто вошёл, уведомления, выход.</summary>
    public ConnectionViewModel Connection { get; }

    public string RevitVersion { get; }
    public string ToadToolsVersion { get; }

    /// <summary>Тема всех окон ToadTools; меняется сразу во всех открытых окнах.</summary>
    public ThemeChoice Theme
    {
        get => _appearance.Theme;
        set
        {
            if (_appearance.Theme == value) return;
            _appearance.Theme = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Размер элементов окон ToadTools.</summary>
    public DensityChoice Density
    {
        get => _appearance.Density;
        set
        {
            if (_appearance.Density == value) return;
            _appearance.Density = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DensityHint));
        }
    }

    /// <summary>Что выбрало «Авто» на экране Revit; при явном выборе размера пусто.</summary>
    public string DensityHint => _appearance.Density == DensityChoice.Auto
        ? $"«Авто» решает по высоте экрана Revit — сейчас: {(_appearance.IsCompact ? "компактный" : "обычный")}"
        : "";

    /// <summary>Подробный журнал: запросы к ToadFlow, запуски команд, шаги панелей.</summary>
    public bool VerboseLog
    {
        get => ToadLog.Verbose;
        set
        {
            if (ToadLog.Verbose == value) return;
            ToadLog.Verbose = value;
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(_paths.Logs);
            _shell.OpenFolder(_paths.Logs);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogStatus = $"Папка журналов недоступна: {exception.Message}";
        }
    }

    private bool CanCreateArchive() => !IsArchiving;

    /// <summary>Архив для разработчика на рабочий стол, затем проводник с выделенным файлом.</summary>
    [RelayCommand(CanExecute = nameof(CanCreateArchive))]
    private async Task CreateArchiveAsync()
    {
        IsArchiving = true;
        LogStatus = null;
        try
        {
            ToadLog.Info("Настройки", "Собирается архив для разработчика");
            ToadLog.Flush();
            var name = $"ToadTools-диагностика-{DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.zip";
            var target = Path.Combine(_paths.Desktop, name);
            var versions = VersionsText();
            await Task.Run(() => DiagnosticsArchive.Create(_paths.AppData, _paths.Logs, versions, target));
            LogStatus = $"Архив сохранён на рабочий стол: {name}";
            _shell.ShowFile(target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogStatus = $"Архив не собран: {exception.Message}";
        }
        finally
        {
            IsArchiving = false;
        }
    }

    /// <summary>Удаляет журналы ToadTools; журнал сбоев Revit-errors.log остаётся.</summary>
    [RelayCommand]
    private void ClearLogs()
    {
        ToadLog.Clear();
        LogStatus = "Журналы очищены";
    }

    /// <summary>Удаляет миниатюры коллизий; «Коллизии» скачают нужные заново.</summary>
    [RelayCommand]
    private void ClearThumbnails()
    {
        if (Directory.Exists(_paths.Thumbnails))
            foreach (var file in SafeFiles(_paths.Thumbnails))
                try
                {
                    File.Delete(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Файл занят панелью — останется до следующей очистки.
                }

        ThumbnailsSize = FormatSize(FolderSize(_paths.Thumbnails));
    }

    [RelayCommand]
    private void OpenWiki() => _shell.OpenUrl(WikiUrl);

    [RelayCommand]
    private void OpenChangelog() => _shell.OpenUrl(ChangelogUrl);

    /// <summary>Содержимое <c>versions.txt</c> архива: версии и адрес сервера, без логина и токена.</summary>
    public string VersionsText() => string.Join(Environment.NewLine,
        $"ToadTools: {ToadToolsVersion}",
        $"Revit: {(string.IsNullOrEmpty(RevitVersion) ? "неизвестно" : RevitVersion)}",
        $"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
        $".NET: {RuntimeInformation.FrameworkDescription}",
        $"Сервер ToadFlow: {(Connection.IsConnected ? Connection.Url : "не подключено")}",
        $"Собран: {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");

    public void Dispose() => _appearance.PropertyChanged -= OnAppearanceChanged;

    private void OnAppearanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IThemePreference.Theme):
                OnPropertyChanged(nameof(Theme));
                break;
            case nameof(IThemePreference.Density):
                OnPropertyChanged(nameof(Density));
                OnPropertyChanged(nameof(DensityHint));
                break;
            case nameof(IThemePreference.IsCompact):
                OnPropertyChanged(nameof(DensityHint));
                break;
        }
    }

    private static long FolderSize(string folder)
    {
        if (!Directory.Exists(folder)) return 0;
        long total = 0;
        foreach (var file in SafeFiles(folder))
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Файл удалили между перечислением и чтением.
            }

        return total;
    }

    private static string[] SafeFiles(string folder)
    {
        try
        {
            return Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        0 => "пусто",
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0", Russian) + " КБ",
        _ => (bytes / (1024.0 * 1024)).ToString("0.0", Russian) + " МБ"
    };
}
