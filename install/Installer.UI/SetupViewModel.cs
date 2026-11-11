using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Threading;

namespace Installer.UI;

public enum SetupPage { Welcome, Versions, Maintenance, Progress, Done, Error }

public enum SetupAction { Install, Change, Repair, Remove }

public enum TaskState { Waiting, Running, Done }

/// <summary>What the user chose: the action and the Revit versions it applies to.</summary>
public sealed class SetupChoice
{
    public SetupChoice(SetupAction action, IReadOnlyList<RevitTarget> selected)
    {
        Action = action;
        Selected = selected;
    }

    public SetupAction Action { get; }
    public IReadOnlyList<RevitTarget> Selected { get; }
}

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class Command : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<bool> _canExecute;

    public Command(Action<object?> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute ?? (() => true);
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute();
    public void Execute(object? parameter) => _execute(parameter);
}

/// <summary>A Revit version row on the versions page.</summary>
public sealed class VersionItem : Observable
{
    private bool _isChecked;
    private bool _running;

    public VersionItem(RevitTarget target, bool isChecked)
    {
        Target = target;
        _isChecked = isChecked;
    }

    public RevitTarget Target { get; }
    public string Name => "Revit " + Target.Year;

    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }

    public bool Running
    {
        get => _running;
        set
        {
            Set(ref _running, value);
            Raise(nameof(Badge));
            Raise(nameof(BadgeTone));
        }
    }

    public string Badge => Running ? "открыт" : Target.Found ? "найден" : "не найден";

    /// <summary>Accent for a found version, warning for a running one, neutral otherwise.</summary>
    public ToadDesign.Tone BadgeTone => Running ? ToadDesign.Tone.Warning : Target.Found ? ToadDesign.Tone.Accent : ToadDesign.Tone.Neutral;
}

/// <summary>A line of the progress page.</summary>
public sealed class TaskItem : Observable
{
    private TaskState _state;

    public TaskItem(string text, int year = 0)
    {
        Text = text;
        Year = year;
    }

    public string Text { get; }

    /// <summary>The Revit version whose files this line stands for; 0 for other steps.</summary>
    public int Year { get; }

    public TaskState State
    {
        get => _state;
        set => Set(ref _state, value);
    }
}

/// <summary>State and commands of the installer window. Lives on the window thread.</summary>
public sealed class SetupViewModel : Observable
{
    private readonly DispatcherTimer _revitTimer;
    private SetupPage _page;
    private SetupAction _action = SetupAction.Install;
    private ISet<int> _running = new HashSet<int>();
    private double _progress;
    private string _status = "Подготовка";
    private bool _cancelRequested;
    private bool _rebootNeeded;
    private string _errorText = "";
    private bool _succeeded;
    private bool _cancelled;

    public SetupViewModel(SetupInfo info, string logFile)
    {
        Info = info;
        LogFile = logFile;
        var checkedByDefault = info.Mode == SetupMode.Install
            ? info.Targets.Where(t => t.Found).ToList()
            : info.Targets.Where(t => t.Installed).ToList();
        if (info.Mode == SetupMode.Upgrade && checkedByDefault.Count == 0) checkedByDefault = info.Targets.Where(t => t.Found).ToList();
        Versions = new ObservableCollection<VersionItem>(info.Targets.Select(t => new VersionItem(t, checkedByDefault.Contains(t))));
        foreach (var item in Versions) item.PropertyChanged += (_, _) => RaiseSelection();

        _page = info.Mode switch
        {
            SetupMode.Maintenance => SetupPage.Maintenance,
            SetupMode.Remove => SetupPage.Progress,
            _ => SetupPage.Welcome
        };
        if (info.Mode == SetupMode.Remove) _action = SetupAction.Remove;

        InstallCommand = new Command(_ => Start(_action), () => CanStart);
        ShowVersionsCommand = new Command(_ => { if (Info.Mode == SetupMode.Maintenance) _action = SetupAction.Change; Page = SetupPage.Versions; });
        BackCommand = new Command(_ => { Page = Info.Mode == SetupMode.Maintenance ? SetupPage.Maintenance : SetupPage.Welcome; });
        RepairCommand = new Command(_ => Start(SetupAction.Repair), () => Blocking.Count == 0);
        RemoveCommand = new Command(_ => Start(SetupAction.Remove), () => Blocking.Count == 0);
        CancelCommand = new Command(_ => Cancel());
        CloseCommand = new Command(_ => CloseRequested?.Invoke(this, EventArgs.Empty));
        RetryCommand = new Command(_ => Retry());
        OpenCommand = new Command(p => Open(p as string));

        _revitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _revitTimer.Tick += (_, _) => RefreshRunning();
        RefreshRunning();
        _revitTimer.Start();
    }

    /// <summary>The user chose what to do; the MSI may start.</summary>
    public event EventHandler<SetupChoice>? Started;

    /// <summary>The window should close.</summary>
    public event EventHandler? CloseRequested;

    public SetupInfo Info { get; }
    public string LogFile { get; }
    public ObservableCollection<VersionItem> Versions { get; }
    public ObservableCollection<TaskItem> Tasks { get; } = new();

    public ICommand InstallCommand { get; }
    public ICommand ShowVersionsCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand RepairCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand OpenCommand { get; }

    public SetupPage Page
    {
        get => _page;
        private set
        {
            Set(ref _page, value);
            Raise(nameof(CanClose));
            Raise(nameof(InstallText));
        }
    }

    // ---------- texts of the first pages ----------

    public string Title => Info.Mode switch
    {
        SetupMode.Upgrade => "Обновление ToadTools",
        SetupMode.Maintenance => "ToadTools " + Info.Version,
        SetupMode.Remove => "Удаление ToadTools",
        _ => "Установка ToadTools"
    };

    public string ProductTitle => "ToadTools " + Info.Version;
    public bool IsUpgrade => Info.Mode == SetupMode.Upgrade;
    public bool HasHighlights => Info.Highlights.Count > 0;

    public string InstallText => Info.Mode == SetupMode.Upgrade && Page == SetupPage.Welcome ? "Обновить"
        : _action == SetupAction.Change ? "Сохранить" : "Установить";

    public IReadOnlyList<int> SelectedYears => Versions.Where(v => v.IsChecked).Select(v => v.Target.Year).ToList();

    public string SelectionTitle => SelectedYears.Count == 0 ? "Revit не найден" : "Revit " + Years(SelectedYears);

    public string SelectionHint
    {
        get
        {
            if (SelectedYears.Count == 0) return "выберите версии, для которых установить плагины";
            var found = Versions.Where(v => v.Target.Found).Select(v => v.Target.Year).ToList();
            var all = SelectedYears.All(found.Contains) && found.All(SelectedYears.Contains);
            var tail = "плагины появятся на вкладке ToadTools";
            return all ? (found.Count == 1 ? "найден" : "найдены") + " на этом компьютере — " + tail : tail;
        }
    }

    public string UpgradeHint => SelectedYears.Count == 0 ? "Версии Revit не выбраны" : "Revit " + Years(SelectedYears);

    public string ScopeTitle => Info.PerMachine ? "Для всех пользователей" : "Для текущего пользователя";

    public string ScopeHint => Info.PerMachine
        ? "нужны права администратора; только для себя — файл " + Info.OtherPackage
        : "права администратора не нужны; для всех — файл " + Info.OtherPackage;

    public string MaintenanceHint
    {
        get
        {
            var installed = Info.Targets.Where(t => t.Installed).Select(t => t.Year).ToList();
            return (installed.Count == 0 ? "" : "Для Revit " + Years(installed) + ". ") + "Что сделать?";
        }
    }

    /// <summary>Running Revit versions whose files this setup replaces or removes: they block the start.</summary>
    public IReadOnlyList<int> Blocking
    {
        get
        {
            var affected = _action switch
            {
                SetupAction.Install when Info.Mode == SetupMode.Install => Enumerable.Empty<int>(),
                SetupAction.Install => Info.Targets.Where(t => t.Installed).Select(t => t.Year).Concat(SelectedYears),
                SetupAction.Change => Info.Targets.Where(t => t.Installed != SelectedYears.Contains(t.Year)).Select(t => t.Year),
                _ => Info.Targets.Where(t => t.Installed).Select(t => t.Year)
            };
            return affected.Where(_running.Contains).Distinct().OrderBy(y => y).ToList();
        }
    }

    public bool IsBlocked => Blocking.Count > 0;
    public string BlockTitle => "Закройте Revit " + Years(Blocking);
    public string BlockText => "Его файлы ToadTools будут заменены. Кнопка станет доступна, как только Revit закроется.";

    /// <summary>Running Revit that only needs a restart afterwards (a new installation).</summary>
    public IReadOnlyList<int> RestartOnly => Info.Mode == SetupMode.Install ? SelectedYears.Where(_running.Contains).ToList() : new List<int>();

    public bool NeedsRestartNote => RestartOnly.Count > 0;
    public string RestartTitle => "Revit " + Years(RestartOnly) + (RestartOnly.Count == 1 ? " открыт" : " открыты");
    public string RestartText => "Плагины в нём появятся после перезапуска.";

    public bool CanStart => SelectedYears.Count > 0 && Blocking.Count == 0
                            || _action == SetupAction.Change && Blocking.Count == 0 && Versions.Any(v => v.IsChecked != v.Target.Installed);

    public bool CanClose => Page != SetupPage.Progress;

    // ---------- progress ----------

    public double Progress
    {
        get => _progress;
        private set
        {
            Set(ref _progress, value);
            Raise(nameof(Percent));
        }
    }

    public string Percent => (int)Math.Round(Progress * 100) + " %";

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string ProgressTitle => _action switch
    {
        SetupAction.Remove => "Удаляем ToadTools " + Info.Version,
        SetupAction.Repair => "Восстанавливаем ToadTools " + Info.Version,
        SetupAction.Change => "Меняем версии Revit",
        _ => (Info.Mode == SetupMode.Upgrade ? "Обновляем ToadTools до " : "Устанавливаем ToadTools ") + Info.Version
    };

    public bool CancelRequested => _cancelRequested;

    // ---------- result ----------

    public string DoneTitle => _action switch
    {
        SetupAction.Remove => "ToadTools удалён",
        SetupAction.Repair => "ToadTools восстановлен",
        SetupAction.Change => "Изменения сохранены",
        _ => Info.Mode == SetupMode.Upgrade ? "ToadTools обновлён до " + Info.Version : "ToadTools " + Info.Version + " установлен"
    };

    public string DoneText
    {
        get
        {
            var text = _action == SetupAction.Remove
                ? "Настройки и профили в %AppData%\\ToadTools остались — пригодятся, если поставите ToadTools снова."
                : "Для Revit " + Years(_finalYears) + ". Откройте Revit — инструменты на вкладке ToadTools.";
            var running = _finalYears.Where(_running.Contains).ToList();
            if (_action != SetupAction.Remove && running.Count > 0)
                text += " Revit " + Years(running) + (running.Count == 1 ? " сейчас открыт: перезапустите его." : " сейчас открыты: перезапустите их.");
            if (_rebootNeeded) text += " Часть файлов была занята — они заменятся после перезагрузки компьютера.";
            return text;
        }
    }

    public bool ShowLinks => _action != SetupAction.Remove;

    public string ErrorTitle => _cancelled
        ? (_action == SetupAction.Remove ? "Удаление прервано" : "Установка прервана")
        : (_action == SetupAction.Remove ? "Удаление не завершено" : "Установка не завершена");

    public string ErrorHint => Info.Mode switch
    {
        SetupMode.Upgrade => "Изменения откатены — на компьютере осталась прежняя версия ToadTools " + Info.PreviousVersion + ".",
        SetupMode.Install => "Изменения откатены — ToadTools не установлен.",
        _ => "Изменения откатены — ToadTools " + Info.Version + " остался как был."
    };

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            Set(ref _errorText, value);
            Raise(nameof(HasErrorText));
        }
    }

    public bool HasErrorText => ErrorText.Length > 0;
    public bool HasLog => LogFile.Length > 0;

    private IReadOnlyList<int> _finalYears = new List<int>();

    // ---------- flow ----------

    private void RaiseSelection()
    {
        foreach (var name in new[] { nameof(SelectedYears), nameof(SelectionTitle), nameof(SelectionHint), nameof(UpgradeHint), nameof(Blocking),
                     nameof(IsBlocked), nameof(BlockTitle), nameof(RestartOnly), nameof(NeedsRestartNote), nameof(RestartTitle), nameof(CanStart) })
            Raise(name);
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshRunning() => UpdateRunning(RevitDetector.Running());

    /// <summary>Takes the Revit versions running now: badges, the "close Revit" block, the restart note.</summary>
    public void UpdateRunning(ISet<int> running)
    {
        if (running.SetEquals(_running)) return;
        _running = running;
        foreach (var item in Versions) item.Running = running.Contains(item.Target.Year);
        RaiseSelection();
    }

    /// <summary>Starts the MSI with the chosen action; called by the commands and, for a removal, by the window.</summary>
    public void Start(SetupAction action)
    {
        _action = action;
        var selected = Versions.Where(v => v.IsChecked).Select(v => v.Target).ToList();
        _finalYears = action switch
        {
            SetupAction.Remove => Info.Targets.Where(t => t.Installed).Select(t => t.Year).ToList(),
            SetupAction.Repair => Info.Targets.Where(t => t.Installed).Select(t => t.Year).ToList(),
            _ => selected.Select(t => t.Year).ToList()
        };

        Tasks.Clear();
        if (action == SetupAction.Install && Info.Mode == SetupMode.Upgrade)
            Tasks.Add(new TaskItem("Удаление ToadTools " + Info.PreviousVersion));
        switch (action)
        {
            case SetupAction.Remove:
                foreach (var target in Info.Targets.Where(t => t.Installed)) Tasks.Add(new TaskItem("Удаление для Revit " + target.Year, target.Year));
                break;
            case SetupAction.Change:
                foreach (var target in Info.Targets.Where(t => t.Installed != selected.Contains(t)))
                    Tasks.Add(new TaskItem((target.Installed ? "Удаление для Revit " : "Revit ") + target.Year, target.Year));
                break;
            case SetupAction.Repair:
                foreach (var target in Info.Targets.Where(t => t.Installed)) Tasks.Add(new TaskItem("Revit " + target.Year, target.Year));
                break;
            default:
                foreach (var target in selected) Tasks.Add(new TaskItem("Revit " + target.Year, target.Year));
                break;
        }

        Tasks.Add(new TaskItem(action == SetupAction.Remove ? "Удаление из «Программ и компонентов»" : "Регистрация в «Программах и компонентах»"));
        foreach (var name in new[] { nameof(ProgressTitle), nameof(DoneTitle), nameof(InstallText) }) Raise(name);
        Page = SetupPage.Progress;
        Started?.Invoke(this, new SetupChoice(action, selected));
    }

    private void Cancel()
    {
        if (Page == SetupPage.Progress)
        {
            _cancelRequested = true;
            Status = "Отменяем…";
            return;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Retry()
    {
        if (Info.Package.Length > 0)
        {
            try
            {
                Process.Start(new ProcessStartInfo("msiexec.exe", "/i \"" + Info.Package + "\"") { UseShellExecute = false });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The package moved or msiexec is blocked: the user starts the file again.
            }
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Open(string? target)
    {
        var url = target switch
        {
            "release" => Info.ReleaseUrl,
            "changelog" => SetupInfo.ChangelogUrl,
            "help" => SetupInfo.HelpUrl,
            "log" => LogFile,
            _ => null
        };
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No browser or no file: nothing to show.
        }
    }

    // ---------- MSI messages (already parsed on the MSI thread) ----------

    private static readonly Regex YearInPath = new(@"\\(20\d\d)(\\|$)");

    public void OnProgress(int[] fields, ProgressCounter counter)
    {
        counter.OnProgress(fields);
        Progress = counter.Progress;
    }

    public void OnActionStart(string action)
    {
        switch (action)
        {
            case "RemoveExistingProducts":
                Status = "Удаление прежней версии";
                Run(Tasks.FirstOrDefault(t => t.Year == 0));
                break;
            case "InstallFiles":
            case "RemoveFiles":
                Status = action == "InstallFiles" ? "Копирование файлов" : "Удаление файлов";
                break;
            case "RegisterProduct":
            case "PublishFeatures":
            case "PublishProduct":
            case "UnpublishFeatures":
            case "UnpublishProduct":
                Status = Tasks.Last().Text;
                Run(Tasks.Last());
                break;
            case "InstallValidate":
            case "CostFinalize":
                Status = "Проверка";
                break;
        }
    }

    public void OnActionData(string directory, ProgressCounter counter)
    {
        counter.OnActionData();
        Progress = counter.Progress;
        var match = YearInPath.Match(directory);
        if (!match.Success) return;
        var year = int.Parse(match.Groups[1].Value);
        var task = Tasks.FirstOrDefault(t => t.Year == year);
        if (task == null || task.State == TaskState.Running) return;
        Status = (Status.StartsWith("Удаление") ? "Удаление файлов для Revit " : "Копирование файлов для Revit ") + year;
        Run(task);
    }

    /// <summary>Marks the task running and everything above it done.</summary>
    private void Run(TaskItem? task)
    {
        if (task == null) return;
        foreach (var item in Tasks)
        {
            if (item == task) break;
            item.State = TaskState.Done;
        }

        task.State = TaskState.Running;
    }

    public void OnError(string text)
    {
        if (ErrorText.Length == 0) ErrorText = text;
    }

    public void OnFilesInUse() => _rebootNeeded = true;

    public void OnEnd(int result)
    {
        _succeeded = result == 1;
        _cancelled = result == 2;
    }

    /// <summary>The MSI finished: show the result page.</summary>
    public void Finish()
    {
        _revitTimer.Stop();
        if (_succeeded)
        {
            foreach (var task in Tasks) task.State = TaskState.Done;
            Progress = 1;
            foreach (var name in new[] { nameof(DoneTitle), nameof(DoneText), nameof(ShowLinks) }) Raise(name);
            Page = SetupPage.Done;
        }
        else
        {
            if (_cancelRequested) _cancelled = true;
            foreach (var name in new[] { nameof(ErrorTitle), nameof(ErrorHint) }) Raise(name);
            Page = SetupPage.Error;
        }
    }

    /// <summary>"Revit 2022 и 2024", "2022, 2024 и 2025".</summary>
    public static string Years(IReadOnlyList<int> years)
    {
        if (years.Count == 0) return "";
        if (years.Count == 1) return years[0].ToString();
        return string.Join(", ", years.Take(years.Count - 1)) + " и " + years[years.Count - 1];
    }
}
