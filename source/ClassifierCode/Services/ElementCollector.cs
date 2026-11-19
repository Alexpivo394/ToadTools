using ClassifierCode.Engine.Profile;
using ClassifierCode.Models;

namespace ClassifierCode.Services;

/// <summary>Собирает элементы категорий профиля, у которых есть параметр кода (в экземпляре или типе).</summary>
public sealed class ElementCollector
{
    /// <summary>Категории профиля, известные этой версии Revit; неизвестные имена — в <paramref name="unknown" />.</summary>
    public static List<BuiltInCategory> Categories(IEnumerable<string> names, List<string>? unknown = null)
    {
        var categories = new List<BuiltInCategory>();
        foreach (var name in names)
        {
            if (Enum.TryParse<BuiltInCategory>(name, out var category) && Enum.IsDefined(typeof(BuiltInCategory), category))
                categories.Add(category);
            else
                unknown?.Add(name);
        }

        return categories;
    }

    public List<Element> Collect(Document doc, View? activeView, RunScope scope, ICollection<ElementId> selection,
        IReadOnlyList<BuiltInCategory> categories, OutputField codeOutput)
    {
        if (categories.Count == 0) return [];
        var categoryIds = new HashSet<ElementId>(categories.Select(category => new ElementId(category)));

        IEnumerable<Element?> candidates;
        if (scope == RunScope.Selection)
        {
            candidates = selection.Select(doc.GetElement);
        }
        else
        {
            var collector = scope == RunScope.ActiveView && activeView != null
                ? new FilteredElementCollector(doc, activeView.Id)
                : new FilteredElementCollector(doc);
            candidates = collector
                .WherePasses(new ElementMulticategoryFilter(categories.ToList()))
                .WhereElementIsNotElementType()
                .ToElements();
        }

        var typeHasParameter = new Dictionary<ElementId, bool>();
        return candidates
            .Where(e => e != null && IsCandidate(e, categoryIds) && HasCodeParameter(doc, e, codeOutput, typeHasParameter))
            .Select(e => e!)
            .ToList();
    }

    private static bool IsCandidate(Element element, HashSet<ElementId> categoryIds)
    {
        return element is not ElementType &&
               !element.ViewSpecific &&
               element.Category != null &&
               categoryIds.Contains(element.Category.Id);
    }

    private static bool HasCodeParameter(Document doc, Element element, OutputField codeOutput, Dictionary<ElementId, bool> typeCache)
    {
        if (ClassifierParameterWriter.Find(element, codeOutput) != null)
            return true;

        var typeId = element.GetTypeId();
        if (typeId == ElementId.InvalidElementId)
            return false;

        if (!typeCache.TryGetValue(typeId, out var has))
        {
            var type = doc.GetElement(typeId);
            has = type != null && ClassifierParameterWriter.Find(type, codeOutput) != null;
            typeCache[typeId] = has;
        }

        return has;
    }
}
