using Autodesk.Revit.DB;
using ParamChecker.Core.Checking;
using ParamChecker.Core.Profiles;
using ParamChecker.Core.Reports;
using ToadTools.UI.Progress;

namespace ParamChecker.Services;

/// <summary>Проверяет открытый документ по правилам профиля.</summary>
public static class ModelChecker
{
    /// <summary>Как часто обновлять полосу элементов: на каждом элементе окно прогресса тормозило бы проверку.</summary>
    private const int ReportEvery = 200;

    /// <param name="viewName">Вид, с которого собираются элементы; пусто — вся модель.</param>
    /// <param name="problemsOnly">В строки отчёта попадают только элементы с незаполненными параметрами.</param>
    /// <exception cref="CheckException">В модели нет вида с таким именем.</exception>
    public static ModelReport Check(Document document, CheckProfile profile, string modelName, string sourcePath, string viewName,
        bool problemsOnly, ToadProgress progress, RunLog log)
    {
        viewName = viewName.Trim();
        View? view = null;
        if (viewName.Length > 0)
        {
            view = ElementSource.FindView(document, viewName) ?? throw new CheckException(MissingView(document, viewName));
        }

        var report = new ModelReport
        {
            ProfileName = profile.Name,
            ModelName = modelName,
            SourcePath = sourcePath,
            ViewName = viewName,
            CheckedAt = DateTime.Now
        };

        var reader = new ParameterReader(document);
        var worksets = document.IsWorkshared ? document.GetWorksetTable() : null;

        for (var index = 0; index < profile.Rules.Count; index++)
        {
            var rule = profile.Rules[index];
            var status = $"Правило {index + 1} из {profile.Rules.Count}: {rule.Title}";
            progress.ItemStatus(status);

            var categories = Categories(rule.Filter, modelName, log);
            var categoryIds = new HashSet<long>(categories.Select(category => (long)category));
            var parameters = rule.Parameters.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToList();
            var result = new RuleReport(rule.Title, parameters);

            var candidates = ElementSource.Candidates(document, view, rule.Filter, categories);
            for (var i = 0; i < candidates.Count; i++)
            {
                if (progress.ShouldAbortItem) throw new OperationCanceledException();
                if (i % ReportEvery == 0) progress.ItemReport(i, candidates.Count, status);

                var element = candidates[i];
                var categoryMatches = element.Category is { } category && categoryIds.Contains(IdValue(category.Id));
                if (!ConditionEvaluator.Accepts(rule.Filter, categoryMatches, name => reader.Read(element, name))) continue;

                var values = parameters.Select(name => reader.Read(element, name)).ToList();
                var complete = result.Count(values);
                if (!problemsOnly || !complete) result.Rows.Add(Row(element, reader, worksets, values));
            }

            result.Rows.Sort(CompareRows);
            report.Rules.Add(result);
            log.Step(modelName, $"правило «{rule.Title}»: элементов {result.Elements}, заполнены полностью {result.CompleteElements}, " +
                                $"заполненность {Percent(result.FillRate)}");
        }

        return report;
    }

    public static string Percent(double? share) => share is { } value ? $"{value:P1}" : "—";

    private static IReadOnlyCollection<BuiltInCategory> Categories(RuleFilter filter, string modelName, RunLog log)
    {
        var result = new List<BuiltInCategory>();
        foreach (var name in filter.Categories)
        {
            if (Enum.TryParse<BuiltInCategory>(name, out var category) && Enum.IsDefined(typeof(BuiltInCategory), category))
                result.Add(category);
            else
                log.Step(modelName, $"категории {name} нет в этой версии Revit — пропущена");
        }

        return result;
    }

    private static ElementRow Row(Element element, ParameterReader reader, WorksetTable? worksets, IReadOnlyList<ParamValue> values)
    {
        var type = reader.TypeOf(element);
        var workset = worksets?.GetWorkset(element.WorksetId)?.Name ?? "";
        return new ElementRow(IdValue(element.Id), element.Category?.Name ?? "", type?.FamilyName ?? "", type?.Name ?? element.Name,
            workset, values);
    }

    private static int CompareRows(ElementRow a, ElementRow b)
    {
        var result = string.Compare(a.Category, b.Category, StringComparison.CurrentCultureIgnoreCase);
        if (result == 0) result = string.Compare(a.Family, b.Family, StringComparison.CurrentCultureIgnoreCase);
        if (result == 0) result = string.Compare(a.Type, b.Type, StringComparison.CurrentCultureIgnoreCase);
        return result != 0 ? result : a.Id.CompareTo(b.Id);
    }

    private static string MissingView(Document document, string viewName)
    {
        var views = ElementSource.ViewNames(document);
        var hint = views.Count == 0
            ? "3D-видов в модели нет"
            : "3D-виды модели: " + string.Join(", ", views.Take(8).Select(v => $"«{v}»")) + (views.Count > 8 ? $" и ещё {views.Count - 8}" : "");
        return $"нет вида «{viewName}». {hint}";
    }

    internal static long IdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
