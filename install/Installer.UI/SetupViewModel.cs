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

public enum SetupPage { Welcome, Maintenance, Progress, Done, Error }

public enum SetupAction { Install, Repair, Remove }

public enum TaskState { Waiting, Running, Done }

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


/// <summary>
///     State and commands of the installer window. Lives on the window thread. The package installs the ToadTools
///     program only; the program downloads the add-in and puts it into the Revit folders itself.
/// </summary>
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
        _page = info.Mode switch
        {
            SetupMode.Maintenance => SetupPage.Maintenance,
            SetupMode.Remove => SetupPage.Progress,
            _ => SetupPage.Welcome
        };
        if (info.Mode == SetupMode.Remove) _action = SetupAction.Remove;

        InstallCommand = new Command(_ => Start(SetupAction.Install), () => !IsBlocked);
        RepairCommand = new Command(_ => Start(SetupAction.Repair));
        RemoveCommand = new Command(_ => Start(SetupAction.Remove), () => !RemoveBlocked);
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
    public event EventHandler<SetupAction>? Started;

    /// <summary>The window should close.</summary>
    public event EventHandler? CloseRequested;

    public SetupInfo Info { get; }
    public string LogFile { get; }
    public ObservableCollection<TaskItem> Tasks { get; } = new();

    public ICommand InstallCommand { get; }
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

    public string ProductTitle => "ToadTools";
    public bool IsUpgrade => Info.Mode == SetupMode.Upgrade;
    public bool HasHighlights => Info.Highlights.Count > 0;
    public string InstallText => Info.Mode == SetupMode.Upgrade ? "Обновить" : "Установить";

    public string PreviousTitle => Info.ReplacesLegacyPlugin ? "Плагин " + Info.PreviousVersion : Info.PreviousVersion;
    public string CurrentTitle => "Программа " + Info.Version;

    public string RevitTitle => Info.RevitYears.Count == 0 ? "Revit не найден" : "Revit " + Years(Info.RevitYears);

    public string RevitHint => Info.RevitYears.Count == 0
        ? "программа поставит плагин, когда Revit появится на компьютере"
        : "программа сама скачает плагин и будет обновлять его, когда Revit закрыт";

    public string ScopeTitle => "Для текущего пользователя";
    public string ScopeHint => "права администратора не нужны; значок программы — в трее";

    public string UpgradeHint => Info.ReplacesLegacyPlugin
        ? "Плагин теперь ставит и обновляет программа ToadTools: она живёт в трее и сама ставит новые версии, когда Revit закрыт."
        : "";

    public bool HasUpgradeHint => UpgradeHint.Length > 0;
    public string MaintenanceHint => "Что сделать?";

    /// <summary>Running Revit versions: the old add-in package's files or the add-in itself are removed, so they block.</summary>
    public IReadOnlyList<int> Blocking => Info.ReplacesLegacyPlugin ? _running.OrderBy(y => y).ToList() : new List<int>();

    public bool IsBlocked => Blocking.Count > 0;
    public bool RemoveBlocked => _running.Count > 0;
    public bool IsRemoveBlocked => Page == SetupPage.Maintenance && RemoveBlocked;
    public string BlockTitle => "Закройте Revit " + Years(_running.OrderBy(y => y).ToList());

    public string BlockText => Page == SetupPage.Maintenance
        ? "Плагин ToadTools будет убран из его папки. Кнопка «Удалить» станет доступна, как только Revit закроется."
        : "Старая версия плагина будет удалена из его папки. Кнопка станет доступна, как только Revit закроется.";

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
        SetupAction.Remove => "Удаляем ToadTools",
        SetupAction.Repair => "Восстанавливаем ToadTools",
        _ => (Info.Mode == SetupMode.Upgrade ? "Обновляем ToadTools до " : "Устанавливаем ToadTools ") + Info.Version
    };

    public bool CancelRequested => _cancelRequested;

    // ---------- result ----------

    public string DoneTitle => _action switch
    {
        SetupAction.Remove => "ToadTools удалён",
        SetupAction.Repair => "ToadTools восстановлен",
        _ => Info.Mode == SetupMode.Upgrade ? "ToadTools обновлён" : "ToadTools установлен"
    };

    public string DoneText
    {
        get
        {
            if (_action == SetupAction.Remove)
                return "Программа и плагин удалены. Настройки и профили в %AppData%\\ToadTools остались — пригодятся, если поставите ToadTools снова.";
            var text = Info.RevitYears.Count == 0
                ? "Программа запущена — её значок в трее. Плагин она поставит, когда на компьютере появится Revit."
                : "Программа запущена — её значок в трее. Она скачает плагин для Revit " + Years(Info.RevitYears) +
                  ": инструменты появятся на вкладке ToadTools при следующем запуске Revit.";
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

    // ---------- flow ----------

    private void RefreshRunning() => UpdateRunning(RevitDetector.Running());

    /// <summary>Takes the Revit versions running now: the "close Revit" block.</summary>
    public void UpdateRunning(ISet<int> running)
    {
        if (running.SetEquals(_running)) return;
        _running = running;
        foreach (var name in new[] { nameof(Blocking), nameof(IsBlocked), nameof(RemoveBlocked), nameof(IsRemoveBlocked), nameof(BlockTitle), nameof(BlockText) })
            Raise(name);
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Starts the MSI with the chosen action; called by the commands and, for a removal, by the window.</summary>
    public void Start(SetupAction action)
    {
        _action = action;
        Tasks.Clear();
        if (action == SetupAction.Install && Info.Mode == SetupMode.Upgrade)
            Tasks.Add(new TaskItem((Info.ReplacesLegacyPlugin ? "Удаление плагина " : "Удаление ToadTools ") + Info.PreviousVersion));
        Tasks.Add(new TaskItem(action switch
        {
            SetupAction.Remove => "Удаление программы и плагина",
            SetupAction.Repair => "Восстановление программы",
            _ => "Установка программы ToadTools"
        }));
        Tasks.Add(new TaskItem(action == SetupAction.Remove ? "Удаление из «Программ и компонентов»" : "Регистрация в «Программах и компонентах»"));
        foreach (var name in new[] { nameof(ProgressTitle), nameof(DoneTitle), nameof(DoneText) }) Raise(name);
        Page = SetupPage.Progress;
        Started?.Invoke(this, action);
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
