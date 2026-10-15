using ToadTools.UI.Progress;
using WorksetCheck.Models;

namespace WorksetCheck.Services;

/// <summary>Проверка и исправление одной модели с выбранными правилами — для окна отчёта.</summary>
public sealed class WorksetCheckSession(Document document, WorksetRules rules, ElementId activeViewId)
{
    public const string Title = "Проверка рабочих наборов";

    public Document Document { get; } = document;

    public static ProgressOptions ProgressOptions => new() { Icon = Wpf.Ui.Controls.SymbolRegular.TaskListSquareLtr24 };

    public CheckResult Check(ToadProgress progress) => new CheckService(Document, rules).Run(progress);

    public FixResult Fix(IReadOnlyList<WorksetIssue> issues, ToadProgress progress) =>
        new FixService(Document, activeViewId).Run(issues, progress);
}
