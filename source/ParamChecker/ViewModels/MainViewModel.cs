using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alexpivo394.Revit.ParamCheck.Profiles;
using Alexpivo394.Revit.ParamCheck.Core;
using Alexpivo394.Revit.ParamCheck.Services;
using ParamChecker.Services;
using ToadTools.UI.Models;
using Alexpivo394.Revit.Export.Sources;
using ToadTools.UI.Services;
using Wpf.Ui.Appearance;

namespace ParamChecker.ViewModels;

/// <summary>Окно ParamChecker: профили слева, выбранный профиль или настройки справа, запуск внизу.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const string ProfilesFilter = "Профили ParamChecker (*.json)|*.json";
    private const string HelpUrl = "https://github.com/Alexpivo394/ToadTools/wiki/ParamChecker";
    private const string DeveloperUrl = "https://t.me/+rrY_k7qHtSplMTUy";

    private readonly CheckerContext _context;
    private readonly SettingsStore _store;
    private readonly DispatcherTimer _saveTimer;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ShowProfile), nameof(ShowEmpty))]
    [NotifyCanExecuteChangedFor(nameof(RunActiveCommand))]
    private ProfileViewModel? _selectedProfile;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowProfile), nameof(ShowEmpty))]
    private bool _isSettingsOpen;

    public MainViewModel(CheckerContext context, SettingsStore store, ProfileSet profiles)
    {
        _context = context;
        _store = store;
        foreach (var profile in profiles.Profiles) Profiles.Add(new ProfileViewModel(context, this, profile));
        SelectedProfile = Profiles.FirstOrDefault();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) => Save();
        context.Changed += (_, _) => ScheduleSave();
        Profiles.CollectionChanged += (_, _) =>
        {
            RefreshRunState();
            ScheduleSave();
        };
        Settings.PropertyChanged += OnSettingsChanged;

        // Пока пользователь настраивает профили, каталог Revit Server обходится в фоне — окно выбора откроется сразу.
        var server = Settings.Server.Trim().Length > 0
            ? Settings.Server.Trim()
            : ServerPickerViewModel.KnownServers(context.RevitYear, AllModelPaths()).FirstOrDefault();
        if (!string.IsNullOrEmpty(server)) _ = ServerCatalog.For(server!, context.RevitYear).EnsureFreshAsync();
    }

    public ObservableCollection<ProfileViewModel> Profiles { get; } = [];

    public CheckerSettings Settings => _context.Settings;

    public bool HasSelection => SelectedProfile is not null;
    public bool ShowProfile => !IsSettingsOpen && SelectedProfile is not null;
    public bool ShowEmpty => !IsSettingsOpen && SelectedProfile is null;

    public string HistoryPlaceholder => Path.Combine(Settings.ReportsFolder, "ParamChecker_База.xlsx");

    private IReadOnlyList<ProfileViewModel> EnabledProfiles => Profiles.Where(p => p.IsEnabled).ToList();

    public int ModelCount => EnabledProfiles.Sum(p => p.Models.Count);

    public string RunText => ModelCount == 0 ? "Проверить" : $"Проверить ({Words.Count(ModelCount, "модель", "модели", "моделей")})";

    public string RunHint => EnabledProfiles.Count == 0
        ? "Отметьте профили, которые нужно проверить"
        : $"Отмечено {Words.Count(EnabledProfiles.Count, "профиль", "профиля", "профилей")}";

    /// <summary>Сохраняет профили и настройки сейчас — перед закрытием окна и перед запуском.</summary>
    public void Save()
    {
        _saveTimer.Stop();
        _store.SaveProfiles(ToSet(Profiles));
        _store.SaveSettings(Settings);
    }

    internal IEnumerable<string> AllModelPaths() => Profiles.SelectMany(p => p.Models).Select(m => m.Path);

    internal void RefreshRunState()
    {
        OnPropertyChanged(nameof(ModelCount));
        OnPropertyChanged(nameof(RunText));
        OnPropertyChanged(nameof(RunHint));
        RunCommand.NotifyCanExecuteChanged();
    }

    internal void DuplicateProfile(ProfileViewModel profile)
    {
        var model = ProfileSerializer.Deserialize(ProfileSerializer.Serialize(ToSet([profile])), SettingsStore.CategoryName).Profiles[0];
        model.Name += " (копия)";
        var copy = new ProfileViewModel(_context, this, model);
        Profiles.Insert(Profiles.IndexOf(profile) + 1, copy);
        SelectedProfile = copy;
    }

    internal void RemoveProfile(ProfileViewModel profile)
    {
        var answer = _context.Windows.Show($"Удалить профиль «{profile.DisplayName}»? Его модели и правила пропадут.",
            DialogButtons.YesNo, DialogIcon.Warning);
        if (answer != "Yes") return;

        var index = Profiles.IndexOf(profile);
        Profiles.Remove(profile);
        if (SelectedProfile is null || !Profiles.Contains(SelectedProfile))
            SelectedProfile = Profiles.Count == 0 ? null : Profiles[Math.Min(index, Profiles.Count - 1)];
    }

    private static ProfileSet ToSet(IEnumerable<ProfileViewModel> profiles) =>
        new() { Profiles = profiles.Select(p => p.ToModel()).ToList() };

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CheckerSettings.DarkTheme))
            ThemeWatcherService.ApplyTheme(Settings.DarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);
        if (e.PropertyName == nameof(CheckerSettings.ReportsFolder)) OnPropertyChanged(nameof(HistoryPlaceholder));
        ScheduleSave();
    }

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new ProfileViewModel(_context, this, new CheckProfile { Name = $"Профиль {Profiles.Count + 1}" });
        Profiles.Add(profile);
        SelectedProfile = profile;
        IsSettingsOpen = false;
    }

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    partial void OnSelectedProfileChanged(ProfileViewModel? value)
    {
        if (value is not null) IsSettingsOpen = false;
    }

    [RelayCommand]
    private void Import()
    {
        var path = _context.Windows.PickOpenFile("Загрузить профили", ProfilesFilter, null);
        if (path is null) return;

        ProfileSet set;
        try
        {
            set = ProfileSerializer.Deserialize(File.ReadAllText(path), SettingsStore.CategoryName);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _context.Windows.Show($"Профили не загружены: {exception.Message}", DialogButtons.OK, DialogIcon.Error);
            return;
        }

        if (set.Profiles.Count == 0)
        {
            _context.Windows.Show("В файле нет профилей.", DialogButtons.OK, DialogIcon.Info);
            return;
        }

        var replace = true;
        if (Profiles.Count > 0)
        {
            var answer = _context.Windows.Show(
                $"В файле {Words.Count(set.Profiles.Count, "профиль", "профиля", "профилей")}.\n\n" +
                "«Да» — заменить ими текущие профили.\n«Нет» — добавить к текущим.", DialogButtons.YesNoCancel, DialogIcon.None);
            if (answer is not ("Yes" or "No")) return;
            replace = answer == "Yes";
        }

        if (replace) Profiles.Clear();
        var first = set.Profiles.Select(p => new ProfileViewModel(_context, this, p)).ToList();
        foreach (var profile in first) Profiles.Add(profile);
        SelectedProfile = first[0];
        Save();
    }

    [RelayCommand]
    private void Export()
    {
        if (Profiles.Count == 0)
        {
            _context.Windows.Show("Сохранять нечего — профилей нет.", DialogButtons.OK, DialogIcon.Info);
            return;
        }

        var path = _context.Windows.PickSaveFile("Сохранить профили", ProfilesFilter, "ParamChecker.json", null);
        if (path is null) return;

        try
        {
            File.WriteAllText(path, ProfileSerializer.Serialize(ToSet(Profiles)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _context.Windows.Show($"Профили не сохранены: {exception.Message}", DialogButtons.OK, DialogIcon.Error);
        }
    }

    [RelayCommand]
    private void ClearProfiles()
    {
        if (Profiles.Count == 0) return;
        var answer = _context.Windows.Show(
            $"Удалить все профили ({Profiles.Count})? Если они ещё понадобятся — сначала сохраните их кнопкой «Экспорт».",
            DialogButtons.YesNo, DialogIcon.Warning);
        if (answer != "Yes") return;

        Profiles.Clear();
        SelectedProfile = null;
    }

    private bool CanRun() => ModelCount > 0;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Run()
    {
        var profiles = EnabledProfiles.Select(p => p.ToModel()).ToList();
        if (!Validate(profiles, needsModels: true)) return;

        Save();
        RunSummary summary;
        try
        {
            summary = new CheckRunner(_context.Application, Settings).Run(profiles);
        }
        catch (Exception exception)
        {
            // Ошибка вне отдельной модели (журнал, папка отчётов) — окно не должно закрыться вместе с ней.
            _context.Windows.Show($"Проверка не выполнена: {exception.Message}", DialogButtons.OK, DialogIcon.Error);
            return;
        }

        ShowSummary(summary);
    }

    private bool CanRunActive() => SelectedProfile is not null;

    /// <summary>Проверка уже открытой модели по выбранному профилю — без открытия моделей профиля.</summary>
    [RelayCommand(CanExecute = nameof(CanRunActive))]
    private void RunActive()
    {
        if (SelectedProfile is null) return;

        var document = _context.Application.ActiveUIDocument?.Document;
        if (document is null || document.IsFamilyDocument)
        {
            _context.Windows.Show("Откройте проект Revit, который нужно проверить.", DialogButtons.OK, DialogIcon.Info);
            return;
        }

        var profile = SelectedProfile.ToModel();
        if (!Validate([profile], needsModels: false)) return;

        Save();
        RunSummary summary;
        try
        {
            summary = new CheckRunner(_context.Application, Settings).RunActive(document, profile);
        }
        catch (Exception exception)
        {
            _context.Windows.Show($"Проверка не выполнена: {exception.Message}", DialogButtons.OK, DialogIcon.Error);
            return;
        }

        var outcome = summary.Outcomes.FirstOrDefault();
        if (outcome?.Status == ModelStatus.Checked && outcome.ReportPath is not null && summary.HistoryError is null)
        {
            var answer = _context.Windows.Show(
                $"{outcome.ModelName}: {Totals(outcome.Report!)}.\n\nОткрыть отчёт?", DialogButtons.YesNo, DialogIcon.Info);
            if (answer == "Yes") Open(outcome.ReportPath);
            return;
        }

        ShowSummary(summary);
    }

    private bool Validate(IReadOnlyList<CheckProfile> profiles, bool needsModels)
    {
        var problems = RunValidator.Problems(profiles, needsModels);
        if (problems.Count == 0) return true;

        var text = string.Join("\n", problems.Take(12).Select(p => "• " + p));
        if (problems.Count > 12) text += $"\n… и ещё {problems.Count - 12}";
        _context.Windows.Show("Перед проверкой поправьте профили:\n\n" + text, DialogButtons.OK, DialogIcon.Warning);
        return false;
    }

    private void ShowSummary(RunSummary summary)
    {
        var outcomes = summary.Outcomes;
        var done = outcomes.Where(o => o.Status == ModelStatus.Checked).ToList();
        var failed = outcomes.Where(o => o.Status == ModelStatus.Failed).ToList();
        var skipped = outcomes.Count(o => o.Status == ModelStatus.Skipped);

        var lines = new List<string> { $"Проверено {done.Count} из {Words.Count(outcomes.Count, "модели", "моделей", "моделей")}." };
        if (skipped > 0) lines.Add($"Пропущено: {skipped}.");
        if (summary.Cancelled) lines.Insert(0, "Проверка прервана.");

        if (done.Count > 0)
        {
            lines.Add("");
            lines.AddRange(done.Take(8).Select(o => $"• {o.ModelName} — {Totals(o.Report!)}"));
            if (done.Count > 8) lines.Add($"… и ещё {done.Count - 8}");
        }

        if (failed.Count > 0)
        {
            lines.Add("");
            lines.Add("Ошибки:");
            lines.AddRange(failed.Take(6).Select(o => $"• {o.ModelName}: {o.Error}"));
            if (failed.Count > 6) lines.Add($"… и ещё {failed.Count - 6} — подробности в журнале");
        }

        if (summary.HistoryError is not null)
        {
            lines.Add("");
            lines.Add(summary.HistoryError);
        }

        lines.Add("");
        lines.Add("Открыть папку с отчётами?");

        var icon = failed.Count > 0 || summary.Cancelled || summary.HistoryError is not null ? DialogIcon.Warning : DialogIcon.Info;
        if (_context.Windows.Show(string.Join("\n", lines), DialogButtons.YesNo, icon) == "Yes") Open(summary.ReportsFolder);
    }

    private static string Totals(ModelReport report) =>
        $"полностью заполнено {ModelChecker.Percent(report.Completeness)} элементов, " +
        $"заполненность {ModelChecker.Percent(report.FillRate)}";

    [RelayCommand]
    private void BrowseReports()
    {
        var folder = _context.Windows.PickFolder(Settings.ReportsFolder, "Папка для отчётов");
        if (folder is not null) Settings.ReportsFolder = folder;
    }

    [RelayCommand]
    private void BrowseHistory()
    {
        var path = _context.Windows.PickSaveFile("Файл базы для дашбордов", "Книга Excel (*.xlsx)|*.xlsx", "ParamChecker_База.xlsx",
            Path.GetDirectoryName(Settings.HistoryPath()));
        if (path is not null) Settings.HistoryFile = path;
    }

    [RelayCommand]
    private void OpenHistory()
    {
        var path = Settings.HistoryPath();
        if (File.Exists(path)) Open(path);
        else _context.Windows.Show("Базы ещё нет — она появится после первой проверки.", DialogButtons.OK, DialogIcon.Info);
    }

    [RelayCommand]
    private void OpenReports()
    {
        Directory.CreateDirectory(Settings.ReportsFolder);
        Open(Settings.ReportsFolder);
    }

    [RelayCommand]
    private void OpenHelp() => Open(HelpUrl);

    [RelayCommand]
    private void WriteDeveloper() => Open(DeveloperUrl);

    private void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
        {
            _context.Windows.Show($"Не удалось открыть {target}: {exception.Message}", DialogButtons.OK, DialogIcon.Warning);
        }
    }
}
