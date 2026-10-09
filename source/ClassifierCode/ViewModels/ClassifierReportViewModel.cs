using System.Text;
using ClassifierCode.Models;

namespace ClassifierCode.ViewModels;

/// <summary>Строка сводки отчёта: подпись и значение.</summary>
public sealed record ReportRow(string Label, string Value);

public sealed partial class ClassifierReportViewModel : ObservableObject
{
    private const int MaxGroups = 40;

    private readonly List<ElementId> _problemIds;

    public event EventHandler? CloseRequested;

    public ClassifierReportViewModel(ClassifierReport report)
    {
        _problemIds = report.ProblemIds;
        Summary = BuildSummary(report);
        Mode = "Режим: " + report.Section.Mode;
        Details = BuildDetails(report);
    }

    public IReadOnlyList<ReportRow> Summary { get; }
    public string Mode { get; }
    public string Details { get; }

    public int ProblemCount => _problemIds.Count;
    public bool HasProblems => ProblemCount > 0;
    public string SelectProblemsLabel => $"Выделить необработанные ({ProblemCount})";

    [RelayCommand]
    private void SelectProblems()
    {
        RevitContext.ActiveUiDocument!.Selection.SetElementIds(_problemIds);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static List<ReportRow> BuildSummary(ClassifierReport report)
    {
        var rows = new List<ReportRow>
        {
            new("Обработано элементов", report.Total.ToString()),
            new("Заполнено экземпляров", report.WrittenInstances.ToString())
        };

        if (report.WrittenTypes > 0)
            rows.Add(new ReportRow("Заполнено типов", report.WrittenTypes.ToString()));
        if (report.KeptExisting > 0)
            rows.Add(new ReportRow("Пропущено (уже заполнено)", report.KeptExisting.ToString()));
        foreach (var pair in report.Skipped.OrderByDescending(p => p.Value))
            rows.Add(new ReportRow("Пропущено (" + pair.Key + ")", pair.Value.ToString()));
        rows.Add(new ReportRow("Не классифицировано", report.Unclassified.Count.ToString()));
        if (report.MissingParameters.Count > 0)
            rows.Add(new ReportRow("Нет параметров ADSK", report.MissingParameters.Count.ToString()));
        if (report.Busy > 0)
            rows.Add(new ReportRow("Заняты другими пользователями", report.Busy.ToString()));
        if (report.TypeConflicts.Count > 0)
            rows.Add(new ReportRow("Типы с разными кодами у экземпляров", report.TypeConflicts.Count.ToString()));
        if (report.Failed.Count > 0)
            rows.Add(new ReportRow("Ошибки записи", report.Failed.Count.ToString()));

        return rows;
    }

    private static string BuildDetails(ClassifierReport report)
    {
        var details = new StringBuilder();
        foreach (var note in report.Notes)
            details.AppendLine(note);
        if (report.Notes.Count > 0)
            details.AppendLine();

        details.AppendLine("Записанные коды:");
        foreach (var pair in report.ByCode.OrderBy(p => p.Key))
            details.AppendLine("  " + pair.Key + " — " + pair.Value);

        AppendGroups(details, "Не классифицировано (категория / семейство: причина):", report.Unclassified);
        AppendGroups(details, "Ошибки записи (категория / семейство):", report.Failed);
        AppendGroups(details, "Нет параметров (категория / семейство):", report.MissingParameters);

        if (report.TypeConflicts.Count > 0)
        {
            details.AppendLine();
            details.AppendLine("Параметр типа, но экземпляры получили разные коды (тип не заполнен):");
            foreach (var conflict in report.TypeConflicts)
                details.AppendLine("  " + conflict);
        }

        return details.ToString().TrimEnd();
    }

    private static void AppendGroups(StringBuilder sb, string header, List<KeyValuePair<ElementId, string>> items)
    {
        if (items.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine(header);
        var groups = items
            .GroupBy(x => x.Value)
            .OrderByDescending(g => g.Count())
            .ToList();
        foreach (var group in groups.Take(MaxGroups))
            sb.AppendLine("  " + group.Key + " — " + group.Count());
        if (groups.Count > MaxGroups)
            sb.AppendLine("  ... ещё групп: " + (groups.Count - MaxGroups));
    }
}
