using ToadTools.UI.Progress;
using WorksetCheck.Models;

namespace WorksetCheck.Services;

/// <summary>
///     Проверяет, что элементы открытой модели лежат в своих рабочих наборах, и для каждого нарушения
///     сразу решает, что с ним сделает «Исправить».
/// </summary>
public sealed class CheckService
{
    private readonly Document _document;
    private readonly WorksetRules _rules;
    private List<Workset> _worksets = [];

    public CheckService(Document document, WorksetRules rules)
    {
        _document = document;
        _rules = rules;
    }

    private enum Kind
    {
        Ignore,
        Link,
        Cad,
        Grid,
        Level,
        Model
    }

    public CheckResult Run(ToadProgress progress)
    {
        _worksets = new FilteredWorksetCollector(_document)
            .OfKind(WorksetKind.UserWorkset)
            .ToWorksets()
            .OrderBy(workset => workset.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var issues = new List<WorksetIssue>();
        var checkedElements = 0;

        progress.Stage("Проверка рабочих наборов");
        for (var i = 0; i < _worksets.Count; i++)
        {
            progress.ThrowIfCancelled();
            var workset = _worksets[i];
            progress.Report(i + 1, _worksets.Count, $"{workset.Name} ({i + 1} из {_worksets.Count})");

            var elements = new FilteredElementCollector(_document)
                .WherePasses(new ElementWorksetFilter(workset.Id))
                .WhereElementIsNotElementType();

            foreach (var element in elements)
            {
                var kind = Classify(element);
                if (kind == Kind.Ignore) continue;

                checkedElements++;
                if (Check(element, kind, workset.Name) is { } issue) issues.Add(issue);
            }
        }

        progress.ThrowIfCancelled();

        var closed = _worksets.Where(workset => !workset.IsOpen).Select(workset => workset.Name).ToList();
        return new CheckResult(issues, checkedElements, closed);
    }

    /// <summary>
    ///     Какие элементы проверяем. Вспомогательные (эскизы, вложенные семейства, элементы с РН родителя)
    ///     пропускаем — их РН нельзя сменить отдельно, и в отчёте они были бы шумом.
    /// </summary>
    private static Kind Classify(Element element)
    {
        switch (element)
        {
            case RevitLinkInstance:
                // Вложенная связь приходит вместе с родительской — её РН отдельно не меняется.
                return element.Document.GetElement(element.GetTypeId()) is RevitLinkType { IsNestedLink: true }
                    ? Kind.Ignore
                    : Kind.Link;
            case ImportInstance:
                return element.ViewSpecific ? Kind.Ignore : Kind.Cad;
            case PointCloudInstance:
                return Kind.Ignore;
        }

        var category = element.Category;
        if (category is null) return Kind.Ignore;
        if (category.Is(BuiltInCategory.OST_Grids)) return Kind.Grid;
        if (category.Is(BuiltInCategory.OST_Levels)) return Kind.Level;

        if (element.ViewSpecific ||
            category.CategoryType != CategoryType.Model ||
            category.Is(BuiltInCategory.OST_SketchLines) ||
            element is FamilyInstance { SuperComponent: not null })
            return Kind.Ignore;

        var worksetParameter = element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
        return worksetParameter is null || worksetParameter.IsReadOnly ? Kind.Ignore : Kind.Model;
    }

    private WorksetIssue? Check(Element element, Kind kind, string workset)
    {
        var inGrids = _rules.IsGridsWorkset(workset);
        var inOpenings = _rules.IsOpeningsWorkset(workset);
        var inLinks = _rules.IsCadWorkset(workset) || WorksetRules.IsLinkFileWorkset(workset);

        switch (kind)
        {
            case Kind.Grid or Kind.Level:
                if (!_rules.ChecksGrids || inGrids) return null;
                return Issue(element, workset,
                    kind == Kind.Grid ? "Ось не в РН осей и уровней" : "Уровень не в РН осей и уровней",
                    TargetByName(_rules.GridsWorkset));

            case Kind.Link:
                return CheckLink(element, workset);

            case Kind.Cad:
                if (!_rules.ChecksCad || _rules.IsCadWorkset(workset)) return null;
                return Issue(element, workset, "CAD-файл не в РН связей DWG", TargetByName(_rules.CadWorkset));

            case Kind.Model:
                if (inLinks)
                    return Issue(element, workset, "Элемент модели в РН связей", Target.On3D);
                if (inGrids)
                    return Issue(element, workset, "Элемент модели в РН осей и уровней", Target.On3D);
                if (inOpenings && !element.Category.Is(BuiltInCategory.OST_GenericModel))
                    return Issue(element, workset, "В РН отверстий допускаются только обобщённые модели", Target.On3D);
                return null;

            default:
                return null;
        }
    }

    /// <summary>У каждой связи свой РН «#Связь имя_файла»: нет такого — создаём, связь в другом — переносим.</summary>
    private WorksetIssue? CheckLink(Element link, string workset)
    {
        var target = WorksetRules.LinkWorkset(TypeName(link));
        if (string.Equals(workset, target, StringComparison.OrdinalIgnoreCase)) return null;

        const string problem = "Связь не в своём РН";
        if (!NamingUtils.IsValidName(target))
            return Issue(link, workset, problem, Target.Manual($"имя «{target}» недопустимо для РН — переименуйте файл связи"));

        return Issue(link, workset, problem, TargetByName(target));
    }

    private Target TargetByName(string name)
    {
        var existing = _worksets.FirstOrDefault(workset =>
            string.Equals(workset.Name, name, StringComparison.OrdinalIgnoreCase));
        return existing is null ? Target.Create(name) : Target.Move(existing.Name);
    }

    private WorksetIssue Issue(Element element, string workset, string problem, Target target)
    {
        var action = target.Action;
        var reason = target.Reason;

        if (action is IssueAction.Move or IssueAction.CreateAndMove)
        {
            // Чужой или устаревший элемент перенести не дадут — сразу говорим об этом.
            var status = WorksharingUtils.GetCheckoutStatus(_document, element.Id, out var owner);
            if (status == CheckoutStatus.OwnedByOtherUser)
            {
                action = IssueAction.Manual;
                reason = $"элемент занят пользователем {owner}";
            }
            else if (WorksharingUtils.GetModelUpdatesStatus(_document, element.Id) == ModelUpdatesStatus.UpdatedInCentral)
            {
                action = IssueAction.Manual;
                reason = "элемент изменён в хранилище — загрузите последние изменения";
            }
        }
        else if (action == IssueAction.ShowOn3D && element.get_BoundingBox(null) is null)
        {
            action = IssueAction.Manual;
            reason = "у элемента нет геометрии для 3D-вида — найдите его по Id";
        }

        return new WorksetIssue
        {
            ElementId = element.Id,
            Category = element.Category?.Name ?? element.GetType().Name,
            Element = Describe(element),
            Workset = workset,
            Problem = problem,
            Action = action,
            TargetWorkset = action is IssueAction.Move or IssueAction.CreateAndMove ? target.Workset : null,
            ManualReason = reason
        };
    }

    private string Describe(Element element)
    {
        return element switch
        {
            RevitLinkInstance or ImportInstance => TypeName(element),
            FamilyInstance instance => $"{instance.Symbol?.FamilyName} : {instance.Name}",
            _ => element.Name
        };
    }

    /// <summary>Имя типа — для связей это имя файла.</summary>
    private string TypeName(Element element)
    {
        var name = _document.GetElement(element.GetTypeId())?.Name ?? element.Name;
        return name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
    }

    private sealed class Target(IssueAction action, string? workset = null, string? reason = null)
    {
        public static readonly Target On3D = new(IssueAction.ShowOn3D);

        public IssueAction Action { get; } = action;
        public string? Workset { get; } = workset;
        public string? Reason { get; } = reason;

        public static Target Move(string workset) => new(IssueAction.Move, workset);
        public static Target Create(string workset) => new(IssueAction.CreateAndMove, workset);
        public static Target Manual(string reason) => new(IssueAction.Manual, reason: reason);
    }
}
