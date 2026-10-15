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
    private List<Workset> _linkWorksets = [];
    private string _separator = "_";

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
        _linkWorksets = _worksets.Where(workset => _rules.IsLinksWorkset(workset.Name)).ToList();
        _separator = DetectSeparator();

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
                return Kind.Link;
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
        var inLinks = _rules.IsLinksWorkset(workset);

        switch (kind)
        {
            case Kind.Grid or Kind.Level:
                if (!_rules.ChecksGrids || inGrids) return null;
                return Issue(element, workset,
                    kind == Kind.Grid ? "Ось не в РН осей и уровней" : "Уровень не в РН осей и уровней",
                    TargetByName(_rules.GridsWorkset));

            case Kind.Link:
                return _rules.ChecksLinks ? CheckLink(element, workset, inLinks) : null;

            case Kind.Cad:
                if (!_rules.ChecksLinks || (inLinks && Sections.HasCadToken(workset))) return null;
                return Issue(element, workset, "CAD-файл не в РН связей DWG", CadTarget());

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

    private WorksetIssue? CheckLink(Element link, string workset, bool inLinks)
    {
        var linkSections = Sections.Find(TypeName(link));
        var inSectionWorkset = inLinks && !Sections.HasCadToken(workset);

        if (linkSections.Count == 0)
        {
            // Раздел по имени не понять — в любом РН связей (кроме DWG) связь считаем на месте.
            if (inSectionWorkset) return null;
            return Issue(link, workset, "Связь не в РН связей", Target.Manual("раздел не определён по имени файла"));
        }

        if (inSectionWorkset && Sections.Find(workset).Intersect(linkSections).Any()) return null;

        if (linkSections.Count > 1)
        {
            var names = string.Join(", ", linkSections);
            return Issue(link, workset, $"Связь не в РН своего раздела ({names})",
                Target.Manual($"по имени файла подходит несколько разделов: {names}"));
        }

        var section = linkSections[0];
        return Issue(link, workset, $"Связь раздела {section} не в РН связей {section}", SectionTarget(section));
    }

    /// <summary>РН связей раздела: если такого нет — создаём «префикс_раздел».</summary>
    private Target SectionTarget(Section section)
    {
        var candidates = _linkWorksets
            .Where(workset => !Sections.HasCadToken(workset.Name) && Sections.Find(workset.Name).Contains(section))
            .ToList();

        // «#Связи_ОВ_ВК» подходит и ОВ, и ВК — если есть РН только под раздел, берём его.
        if (candidates.Count > 1)
        {
            var exact = candidates.Where(workset => Sections.Find(workset.Name).Count == 1).ToList();
            if (exact.Count > 0) candidates = exact;
        }

        return candidates.Count switch
        {
            0 => Target.Create(_rules.LinksPrefix + _separator + section.Name),
            1 => Target.Move(candidates[0].Name),
            _ => Target.Manual("подходит несколько РН: " + string.Join(", ", candidates.Select(workset => workset.Name)))
        };
    }

    private Target CadTarget()
    {
        var candidates = _linkWorksets.Where(workset => Sections.HasCadToken(workset.Name)).ToList();
        return candidates.Count switch
        {
            0 => Target.Create(_rules.LinksPrefix + _separator + Sections.CadToken),
            1 => Target.Move(candidates[0].Name),
            _ => Target.Manual("подходит несколько РН DWG: " + string.Join(", ", candidates.Select(workset => workset.Name)))
        };
    }

    private Target TargetByName(string name)
    {
        var existing = _worksets.FirstOrDefault(workset =>
            string.Equals(workset.Name, name, StringComparison.OrdinalIgnoreCase));
        return existing is null ? Target.Create(name) : Target.Move(existing.Name);
    }

    /// <summary>
    ///     Разделитель между префиксом и разделом в новых РН — как в уже существующих:
    ///     «#Связи_АР» → «_», «#АР» → без разделителя. По умолчанию «_».
    /// </summary>
    private string DetectSeparator()
    {
        return _linkWorksets
                   .Select(workset => workset.Name)
                   .Select(name => (name, index: Sections.FirstMarkerIndex(name)))
                   .Where(pair => pair.index >= _rules.LinksPrefix.Length)
                   .Select(pair => pair.name.Substring(_rules.LinksPrefix.Length, pair.index - _rules.LinksPrefix.Length))
                   .Where(separator => separator.All(c => !char.IsLetterOrDigit(c)))
                   .GroupBy(separator => separator)
                   .OrderByDescending(group => group.Count())
                   .Select(group => group.Key)
                   .FirstOrDefault()
               ?? "_";
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
