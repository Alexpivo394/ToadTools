using System.Diagnostics;
using System.Globalization;
using SmartTags.Models;
using Wpf.Ui.Controls;

namespace SmartTags.ViewModels;

/// <summary>Число в шапке отчёта.</summary>
public sealed record ReportCounter(string Label, int Value);

public sealed partial class SmartTagsReportViewModel : ObservableObject
{
    private readonly RunResult _result;

    public event EventHandler? CloseRequested;

    public SmartTagsReportViewModel(RunResult result, SmartTagsSettings settings)
    {
        _result = result;
        var problems = result.Count(ItemStatus.PlacedWithConflicts) + result.Count(ItemStatus.Failed);

        Counters =
        [
            new ReportCounter("Обработано", result.Items.Count),
            new ReportCounter("Размещено", result.Count(ItemStatus.Placed)),
            new ReportCounter("Пропущено", result.Count(ItemStatus.Skipped)),
            new ReportCounter("Проблемных", problems)
        ];

        (Headline, Message, Severity) = Describe(result, settings, problems);

        Reasons = result.Items
            .Where(item => item.Status != ItemStatus.Placed)
            .GroupBy(item => item.Status == ItemStatus.PlacedWithConflicts ? "Конфликт не устранён" : FirstPart(item.Reason))
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key}: {group.Count()}")
            .ToList();

        Items = result.Items
            .OrderBy(item => item.Status switch
            {
                ItemStatus.Failed => 0,
                ItemStatus.PlacedWithConflicts => 1,
                ItemStatus.Skipped => 2,
                _ => 3
            })
            .ThenBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Statistics = BuildStatistics(result);
    }

    public string Headline { get; }
    public string Message { get; }
    public InfoBarSeverity Severity { get; }
    public IReadOnlyList<ReportCounter> Counters { get; }
    public IReadOnlyList<string> Reasons { get; }
    public IReadOnlyList<ItemReport> Items { get; }
    public string Statistics { get; }

    public bool HasReasons => Reasons.Count > 0;
    public bool HasItems => Items.Count > 0;
    public bool HasLog => _result.LogPath is not null;

    public int ProblemCount => _result.ProblemElementIds.Count;
    public bool HasProblems => ProblemCount > 0;
    public string SelectProblemsLabel => $"Выделить проблемные ({ProblemCount})";

    /// <summary>Элементы, которые нужно выделить в модели после закрытия отчёта.</summary>
    public List<ElementId>? ElementsToSelect { get; private set; }

    [RelayCommand]
    private void SelectProblems()
    {
        ElementsToSelect = _result.ProblemElementIds;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectItem(ItemReport? item)
    {
        if (item is null || item.ElementIds.Count == 0) return;

        ElementsToSelect = item.ElementIds.ToList();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenLog()
    {
        if (_result.LogPath is { } path) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private static (string, string, InfoBarSeverity) Describe(RunResult result, SmartTagsSettings settings, int problems)
    {
        if (result.Cancelled)
            return ("Запуск отменён", "Изменения в модели откачены, марки не созданы.", InfoBarSeverity.Warning);

        if (!result.Completed)
            return ("Запуск завершился с ошибкой", (result.Error ?? "Неизвестная ошибка.") +
                                                   "\nИзменения в модели откачены.", InfoBarSeverity.Error);

        var headline = "Марки расставлены: " + settings.CategoriesText;
        if (settings.OnlyElements is not null) headline += " (только выбранные)";

        string message;
        InfoBarSeverity severity;
        if (result.Items.Count == 0)
        {
            message = settings.OnlyElements is null
                ? "На виде не найдено подходящих элементов выбранных категорий."
                : "Среди выбранных элементов не найдено подходящих для маркировки.";
            severity = InfoBarSeverity.Warning;
        }
        else if (problems > 0)
        {
            message = "Для части элементов не нашлось положения без пересечений — марки поставлены с конфликтом. " +
                      "«Выделить проблемные» — чтобы поправить их вручную.";
            severity = InfoBarSeverity.Warning;
        }
        else
        {
            message = "Марки размещены без пересечений текста и выносок. Весь запуск отменяется одним Ctrl+Z.";
            severity = InfoBarSeverity.Success;
        }

        if (result.LockedCopyId is not null)
            message += "\nДля блокировки создана копия 3D-вида с той же ориентацией — она открыта как активный вид.";
        else if (result.ViewLocked)
            message += "\n3D-вид заблокирован (ориентация сохранена).";

        return (headline, message, severity);
    }

    private static string BuildStatistics(RunResult result)
    {
        var seconds = (result.ElapsedMs / 1000.0).ToString("0.0", CultureInfo.CurrentCulture);
        if (result.LayoutStats is not { } stats) return $"Время: {seconds} с";

        var text = $"Раскладка: марок {stats.Items}, препятствий {stats.Obstacles}, итераций {stats.Iterations}. " +
                   $"Время: {seconds} с";
        return stats.ElapsedMs >= stats.BudgetMs ? text + " (лимит времени исчерпан)" : text;
    }

    /// <summary>«Короткий участок: 120 мм» → «Короткий участок» — для группировки причин.</summary>
    private static string FirstPart(string reason)
    {
        var colon = reason.IndexOf(':');
        return colon > 0 ? reason.Substring(0, colon) : reason;
    }
}
