using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ToadExporter.Core;
using ToadExporter.Services;
using ToadExporter.ViewModels.Tree;
using ToadTools.UI.Models;
using ToadTools.UI.Services;
using Wpf.Ui.Appearance;
using ToadTools.UI.RevitServer;

namespace ToadExporter.ViewModels;

/// <summary>Окно выгрузки: источник, дерево моделей, настройки и запуск.</summary>
public sealed partial class ToadExporterViewModel : ObservableObject
{
    private const string Title = "ToadExporter";
    private const string ConfigFilter = "Конфигурация ToadExporter (*.json)|*.json";

    private readonly UIApplication _application;
    private readonly SettingsStore _store;
    private bool _applyingConfig;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasLastLog))] private string? _lastLogPath;

    public ToadExporterViewModel(UIApplication application, SettingsStore store)
    {
        _application = application;
        _store = store;
        RevitYear = application.Application.VersionNumber;

        Settings = store.Load();
        Settings.PropertyChanged += OnSettingsChanged;

        NwcAvailable = NwcExporter.IsAvailable();
        Tree.NwcEnabled = NwcAvailable;
        Tree.PropertyChanged += OnTreeChanged;

        foreach (var server in RsnConfig.ReadServers(RsnConfig.DefaultPath(RevitYear))) Servers.Add(server);
        if (Settings.Server.Length == 0 && Servers.Count > 0) Settings.Server = Servers[0];

        UpdateLastLog();
        _ = ReloadAsync();
    }

    public ExportSettings Settings { get; }

    public ModelTreeViewModel Tree { get; } = new();

    public ObservableCollection<string> Servers { get; } = [];

    public string RevitYear { get; }

    public string ServerVersionText => $"Revit Server {RevitYear}";

    public bool NwcAvailable { get; }

    public bool PurgeAvailable => RvtSaver.PurgeAvailable;

    public string PurgeHint => PurgeAvailable ? "Удаляет неиспользуемые типы, семейства и материалы" : "Доступно с Revit 2024";

    public IReadOnlyList<Option<NwcParameters>> ParameterOptions { get; } =
    [
        new(NwcParameters.None, "Нет"),
        new(NwcParameters.Elements, "Элементы"),
        new(NwcParameters.All, "Все")
    ];

    public IReadOnlyList<Option<bool>> CoordinateOptions { get; } =
    [
        new(true, "Общие"),
        new(false, "Внутренние")
    ];

    public bool IsServerMode
    {
        get => Settings.SourceKind == SourceKind.Server;
        set => Settings.SourceKind = value ? SourceKind.Server : SourceKind.Folder;
    }

    public bool IsFolderMode
    {
        get => !IsServerMode;
        set => IsServerMode = !value;
    }

    public int ModelCount => Tree.CollectJobs(IsServerMode).Count;

    public string ExportText => ModelCount == 0 ? "Выгрузить" : $"Выгрузить ({ModelCount} {Models(ModelCount)})";

    public string SelectionText => $"Выбрано: {Tree.RvtCount} в RVT, {Tree.NwcCount} в NWC";

    public bool HasLastLog => LastLogPath is not null;

    public bool HasModels => Tree.Roots.Count > 0;

    public string EmptyText => Tree.IsLoading
        ? "Загрузка…"
        : Tree.HasError
            ? ""
            : IsServerMode
            ? Settings.Server.Length == 0 ? "Выберите сервер" : "На сервере нет моделей"
            : Settings.SourceFolder.Length == 0 ? "Выберите папку с моделями" : "В папке нет моделей .rvt";

    public void SaveState() => _store.Save(Settings);

    [RelayCommand]
    private Task Reload() => ReloadAsync();

    [RelayCommand]
    private void SelectServer() => IsServerMode = true;

    [RelayCommand]
    private void SelectFolder() => IsServerMode = false;

    [RelayCommand]
    private void BrowseSource()
    {
        var folder = FolderPicker.PickFolder(Settings.SourceFolder, "Папка с моделями");
        if (folder is not null) Settings.SourceFolder = folder;
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        var folder = FolderPicker.PickFolder(Settings.OutputFolder, "Папка для результатов");
        if (folder is not null) Settings.OutputFolder = folder;
    }

    [RelayCommand]
    private Task SelectAllRvt() => Tree.SetAllAsync(true, null);

    [RelayCommand]
    private Task SelectAllNwc() => NwcAvailable ? Tree.SetAllAsync(null, true) : Task.CompletedTask;

    [RelayCommand]
    private Task ClearAll() => Tree.SetAllAsync(false, false);

    [RelayCommand]
    private void OpenLastLog()
    {
        if (LastLogPath is not null) OpenLog(LastLogPath);
    }

    [RelayCommand]
    private void ExportConfig()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить конфигурацию",
            Filter = ConfigFilter,
            FileName = "ToadExporter.json",
            InitialDirectory = ConfigFolder()
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            Core.ExportConfig.Save(dialog.FileName, Settings, Tree.CheckedModels());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ToadDialogService.Show(Title, $"Конфигурация не сохранена: {exception.Message}", DialogButtons.OK, DialogIcon.Error);
        }
    }

    [RelayCommand]
    private async Task ImportConfig()
    {
        var dialog = new OpenFileDialog { Title = "Загрузить конфигурацию", Filter = ConfigFilter, InitialDirectory = ConfigFolder() };
        if (dialog.ShowDialog() != true) return;

        IReadOnlyList<CheckedModel> models;
        _applyingConfig = true; // источник перечитывается один раз — после всех настроек
        try
        {
            models = Core.ExportConfig.Load(dialog.FileName, Settings);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ToadDialogService.Show(Title, $"Конфигурация не загружена: {exception.Message}", DialogButtons.OK, DialogIcon.Error);
            return;
        }
        finally
        {
            _applyingConfig = false;
        }

        await ReloadAsync();
        if (Tree.HasError || models.Count == 0) return;

        var missing = await Tree.ApplyCheckedAsync(models);
        RefreshTreeState();
        if (missing.Count == 0) return;

        var list = string.Join("\n", missing.Take(10).Select(model => "• " + model.ModelPath));
        if (missing.Count > 10) list += $"\n… и ещё {missing.Count - 10}";
        ToadDialogService.Show(Title, $"Этих моделей из конфигурации в источнике нет, они не отмечены:\n\n{list}",
            DialogButtons.OK, DialogIcon.Warning);
    }

    private string? ConfigFolder() => Directory.Exists(Settings.OutputFolder) ? Settings.OutputFolder : null;

    [RelayCommand]
    private void Export()
    {
        var jobs = Tree.CollectJobs(IsServerMode);

        var errors = RunValidator.Errors(jobs, Settings);
        if (errors.Count > 0)
        {
            ToadDialogService.Show(Title, string.Join("\n", errors), DialogButtons.OK, DialogIcon.Warning);
            return;
        }

        var duplicates = RunValidator.Duplicates(jobs);
        if (duplicates.Count > 0)
        {
            var list = string.Join("\n", duplicates.Select(group =>
                $"• {group.Key}: " + string.Join(", ", group.Select(job => Path.GetDirectoryName(job.SourcePath.Replace('/', '\\'))))));
            var answer = ToadDialogService.Show(Title,
                "Эти модели называются одинаково и перезапишут друг друга в папке результатов " +
                $"(останется последняя):\n\n{list}\n\nПродолжить?", DialogButtons.YesNo, DialogIcon.Warning);
            if (answer != "Yes") return;
        }

        SaveState();
        RunSummary summary;
        try
        {
            summary = new ExportRunner(_application).Run(jobs, Settings);
        }
        catch (Exception exception)
        {
            // Ошибка вне отдельной модели (журнал, временная папка) — окно не должно закрыться вместе с ней.
            ToadDialogService.Show(Title, $"Выгрузка не выполнена: {exception.Message}", DialogButtons.OK, DialogIcon.Error);
            return;
        }

        LastLogPath = summary.LogPath;
        ShowSummary(summary);
    }

    private void ShowSummary(RunSummary summary)
    {
        var results = summary.Results;
        var text = ExportLog.SummaryText(results).Replace(", ", "\n");

        var notes = results.Where(r => r.Note is not null && !r.SkippedByUser).ToList();
        if (notes.Count > 0) text += "\n\n" + string.Join("\n", notes.Take(8).Select(r => $"• {r.Name}: {r.Note}"));
        if (notes.Count > 8) text += $"\n… и ещё {notes.Count - 8} — подробности в журнале";
        if (summary.Cancelled) text = "Выгрузка прервана.\n\n" + text;

        var icon = results.Any(r => r.HasErrors) ? DialogIcon.Warning : DialogIcon.Info;
        if (ToadDialogService.Show(Title, text + "\n\nОткрыть журнал?", DialogButtons.YesNo, icon) == "Yes")
            OpenLog(summary.LogPath);
    }

    private async Task ReloadAsync()
    {
        if (IsServerMode)
        {
            var server = Settings.Server.Trim();
            if (server.Length == 0) Clear();
            else await Tree.LoadAsync(new RevitServerSource(server, RevitYear));
        }
        else
        {
            var folder = Settings.SourceFolder.Trim();
            if (folder.Length == 0) Clear();
            else await Tree.LoadAsync(new FolderSource(folder));
        }

        RefreshTreeState();
    }

    private void Clear()
    {
        Tree.Roots.Clear();
        Tree.Error = null;
        RefreshTreeState();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ExportSettings.SourceKind):
                OnPropertyChanged(nameof(IsServerMode));
                OnPropertyChanged(nameof(IsFolderMode));
                if (!_applyingConfig) _ = ReloadAsync();
                break;
            case nameof(ExportSettings.Server):
            case nameof(ExportSettings.SourceFolder):
                if (!_applyingConfig) _ = ReloadAsync();
                break;
            case nameof(ExportSettings.OutputFolder):
                UpdateLastLog();
                break;
            case nameof(ExportSettings.DarkTheme):
                ThemeWatcherService.ApplyTheme(Settings.DarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);
                break;
        }
    }

    private void OnTreeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModelTreeViewModel.RvtCount) or nameof(ModelTreeViewModel.NwcCount)
            or nameof(ModelTreeViewModel.IsLoading))
            RefreshTreeState();
    }

    private void RefreshTreeState()
    {
        OnPropertyChanged(nameof(ModelCount));
        OnPropertyChanged(nameof(ExportText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(HasModels));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void UpdateLastLog()
    {
        try
        {
            LastLogPath = Directory.Exists(Settings.OutputFolder)
                ? new DirectoryInfo(Settings.OutputFolder).GetFiles("ToadExporter_*.log")
                    .OrderByDescending(file => file.LastWriteTime)
                    .FirstOrDefault()?.FullName
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LastLogPath = null;
        }
    }

    /// <summary>Открывает журнал; пока шла выгрузка или висел итог, его могли переместить или удалить.</summary>
    private void OpenLog(string path)
    {
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("файл не найден");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or Win32Exception or UnauthorizedAccessException)
        {
            ToadDialogService.Show(Title, $"Журнал не открыт ({exception.Message}):\n{path}", DialogButtons.OK, DialogIcon.Warning);
            UpdateLastLog();
        }
    }

    private static string Models(int count) => (count % 100, count % 10) switch
    {
        ( >= 11 and <= 14, _) => "моделей",
        (_, 1) => "модель",
        (_, >= 2 and <= 4) => "модели",
        _ => "моделей"
    };
}

public sealed record Option<T>(T Value, string Title);
