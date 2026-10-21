using Autodesk.Revit.DB;
using ParamChecker.Core.Profiles;

namespace ParamChecker.Services;

/// <summary>Элементы, из которых правило отбирает проверяемые: вид или вся модель, при необходимости — по категориям.</summary>
public static class ElementSource
{
    /// <summary>Помещения, пространства и зоны не видны на 3D-видах — их берём из всей модели.</summary>
    private static readonly HashSet<BuiltInCategory> Spatial =
    [
        BuiltInCategory.OST_Rooms,
        BuiltInCategory.OST_MEPSpaces,
        BuiltInCategory.OST_Areas
    ];

    /// <summary>Вид по имени без учёта регистра; из одноимённых предпочитается 3D-вид.</summary>
    public static View? FindView(Document document, string name) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(view => !view.IsTemplate && string.Equals(view.Name.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(view => view is View3D ? 0 : 1)
            .FirstOrDefault();

    /// <summary>Имена 3D-видов модели — подсказка, когда нужного вида нет.</summary>
    public static IReadOnlyList<string> ViewNames(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .Where(view => !view.IsTemplate)
            .Select(view => view.Name)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    ///     Кандидаты для правила. Если правило требует категорию, отбор по категориям делает Revit (быстрый фильтр),
    ///     иначе берутся все элементы модели (вида), а категорию проверяет правило.
    /// </summary>
    /// <param name="view">Вид; null — вся модель.</param>
    public static IReadOnlyList<Element> Candidates(Document document, View? view, RuleFilter filter, IReadOnlyCollection<BuiltInCategory> categories)
    {
        var categoryRequired = filter.Mode is SelectionMode.CategoriesOnly or SelectionMode.CategoriesAndParameters;
        var visible = categories.Where(category => !Spatial.Contains(category)).ToList();
        var result = new List<Element>();

        if (!categoryRequired)
        {
            result.AddRange(Collector(document, view).Where(element => view is null ? IsModelElement(element) : element.Category is not null));
        }
        else if (visible.Count > 0)
        {
            result.AddRange(Collector(document, view).WherePasses(new ElementMulticategoryFilter(visible)));
        }

        var spatial = categories.Where(Spatial.Contains).ToList();
        if (spatial.Count > 0)
        {
            var seen = new HashSet<ElementId>(result.Select(element => element.Id));
            result.AddRange(new FilteredElementCollector(document)
                .WherePasses(new ElementMulticategoryFilter(spatial))
                .WhereElementIsNotElementType()
                .Where(element => element is not SpatialElement { Area: <= 0 } && seen.Add(element.Id)));
        }

        return result;
    }

    private static FilteredElementCollector Collector(Document document, View? view) =>
        (view is null ? new FilteredElementCollector(document) : new FilteredElementCollector(document, view.Id))
        .WhereElementIsNotElementType();

    /// <summary>Во всей модели без отбора по категориям — только элементы модели, без видов, листов и служебного.</summary>
    private static bool IsModelElement(Element element) =>
        element.Category is { CategoryType: CategoryType.Model } && element is not ElementType;
}
