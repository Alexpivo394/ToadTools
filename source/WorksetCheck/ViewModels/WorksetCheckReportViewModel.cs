using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using ToadTools.UI.Models;
using ToadTools.UI.Progress;
using ToadTools.UI.Services;
using WorksetCheck.Models;
using WorksetCheck.Services;
using Wpf.Ui.Controls;

namespace WorksetCheck.ViewModels;

/// <summary>Число в шапке отчёта.</summary>
public sealed record ReportCounter(string Label, int Value);

/// <summary>
///     Отчёт проверки. «Исправить» работает прямо из окна: исправление → повторная проверка → обновлённый список.
/// </summary>
public sealed partial class WorksetCheckReportViewModel : ObservableObject
{
    private readonly WorksetCheckSession _session;
    private CheckResult _result;

    [ObservableProperty] private string _headline = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private InfoBarSeverity _severity;
    [ObservableProperty] private IReadOnlyList<ReportCounter> _counters = [];
    [ObservableProperty] private IReadOnlyList<WorksetIssue> _items = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasView))]
    private ElementId? _viewId;

    public WorksetCheckReportViewModel(WorksetCheckSession session, CheckResult result)
    {
        _session = session;
        _result = result;
        Show(result, null);
    }

    public event EventHandler? CloseRequested;

    public bool CanFix => _result.Issues.Any(issue => issue.IsAutoFix || issue.Action == IssueAction.ShowOn3D);
    public bool HasView => ViewId is not null;
    public bool HasItems => Items.Count > 0;

    /// <summary>Вид, который нужно открыть после закрытия отчёта.</summary>
    public ElementId? ViewToOpen { get; private set; }

    /// <summary>Элементы, которые нужно выделить в модели после закрытия отчёта.</summary>
    public List<ElementId>? ElementsToSelect { get; private set; }

    [RelayCommand]
    private void Fix()
    {
        FixResult fix;
        try
        {
            using var progress = ToadProgress.Show(WorksetCheckSession.Title, WorksetCheckSession.ProgressOptions);
            fix = _session.Fix(_result.Issues, progress);
        }
        catch (OperationCanceledException)
        {
            Message = "Исправление прервано — изменения в модели откачены.";
            Severity = InfoBarSeverity.Warning;
            return;
        }
        catch (Exception exception)
        {
            Message = "Исправление завершилось с ошибкой, изменения откачены: " + exception.Message;
            Severity = InfoBarSeverity.Error;
            return;
        }

        ViewId = fix.ViewId;

        try
        {
            using var progress = ToadProgress.Show(WorksetCheckSession.Title, WorksetCheckSession.ProgressOptions);
            _result = _session.Check(progress);
        }
        catch (OperationCanceledException)
        {
            Show(_result, Describe(fix) + "\nПовторная проверка прервана — список ниже показывает состояние до исправления.");
            return;
        }

        Show(_result, Describe(fix));
    }

    [RelayCommand]
    private void Save()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"{_session.Document.Title} — проверка РН.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            ReportExporter.Save(dialog.FileName, _session.Document.Title, _result.Issues);
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (IOException exception)
        {
            ToadDialogService.Show(WorksetCheckSession.Title,
                "Не удалось сохранить отчёт: " + exception.Message + "\nВозможно, файл открыт в Excel.",
                DialogButtons.OK, DialogIcon.Error);
        }
    }

    [RelayCommand]
    private void OpenView()
    {
        ViewToOpen = ViewId;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectItem(WorksetIssue? issue)
    {
        if (issue is null) return;
        ElementsToSelect = [issue.ElementId];
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Show(CheckResult result, string? fixSummary)
    {
        var auto = result.Issues.Count(issue => issue.IsAutoFix);
        var on3D = result.Count(IssueAction.ShowOn3D);
        var manual = result.Count(IssueAction.Manual);

        Counters =
        [
            new ReportCounter("Замечаний", result.Issues.Count),
            new ReportCounter("Исправится сам", auto),
            new ReportCounter("На 3D-вид", on3D),
            new ReportCounter("Вручную", manual)
        ];

        Items = result.Issues
            .OrderBy(issue => issue.Action)
            .ThenBy(issue => issue.Workset, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(issue => issue.Category, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Headline = result.Issues.Count == 0
            ? "Рабочие наборы в порядке"
            : $"Найдено замечаний: {result.Issues.Count}";

        var lines = new List<string>();
        if (fixSummary is not null) lines.Add(fixSummary);

        if (result.Issues.Count == 0)
            lines.Add($"Проверено элементов: {result.CheckedElements}. Все лежат в своих рабочих наборах.");
        else
        {
            if (auto > 0) lines.Add($"«Исправить» перенесёт {auto} эл. в нужные РН (недостающие РН будут созданы).");
            if (on3D > 0)
                lines.Add($"{on3D} эл. лежат не в своём РН, но какой нужен — не понять: «Исправить» соберёт их на 3D-виде «{FixService.ViewName}».");
            if (manual > 0) lines.Add($"{manual} эл. нужно поправить вручную — причина в столбце «Действие».");
        }

        if (result.ClosedWorksets.Count > 0)
            lines.Add("Закрыты РН: " + string.Join(", ", result.ClosedWorksets) +
                      " — их элементы не проверялись. Откройте их и запустите проверку снова.");

        Message = string.Join("\n", lines);
        Severity = result.Issues.Count == 0
            ? result.ClosedWorksets.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success
            : InfoBarSeverity.Warning;

        OnPropertyChanged(nameof(CanFix));
        OnPropertyChanged(nameof(HasItems));
    }

    private static string Describe(FixResult fix)
    {
        var parts = new List<string> { $"Исправлено: перенесено {fix.Moved} эл." };
        if (fix.CreatedWorksets.Count > 0) parts.Add("создан РН " + string.Join(", ", fix.CreatedWorksets.Select(name => $"«{name}»")));
        if (fix.ShownOn3D > 0) parts.Add($"на 3D-вид «{FixService.ViewName}» собрано {fix.ShownOn3D} эл.");

        var text = string.Join(", ", parts) + ".";
        if (fix.Failures.Count > 0)
            text += $"\nНе удалось перенести {fix.Failures.Count} эл.:\n" + string.Join("\n", fix.Failures.Take(5)) +
                    (fix.Failures.Count > 5 ? "\n…" : string.Empty);
        return text;
    }
}
