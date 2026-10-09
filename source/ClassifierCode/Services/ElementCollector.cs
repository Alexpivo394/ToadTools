using ClassifierCode.Models;

namespace ClassifierCode.Services;

/// <summary>
///     Собирает элементы категорий <see cref="ClassifierCategories" />, у которых есть параметр кода
///     (в экземпляре или типе).
/// </summary>
public sealed class ElementCollector
{
    private static readonly HashSet<ElementId> CategoryIds =
        new(ClassifierCategories.All.Select(category => new ElementId(category)));

    public List<Element> Collect(Document doc, View? activeView, RunScope scope, ICollection<ElementId> selection)
    {
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
                .WherePasses(new ElementMulticategoryFilter(ClassifierCategories.All.ToList()))
                .WhereElementIsNotElementType()
                .ToElements();
        }

        var typeHasParameter = new Dictionary<ElementId, bool>();
        return candidates
            .Where(e => e != null && IsCandidate(e) && HasCodeParameter(doc, e, typeHasParameter))
            .Select(e => e!)
            .ToList();
    }

    private static bool IsCandidate(Element element)
    {
        return element is not ElementType &&
               !element.ViewSpecific &&
               element.Category != null &&
               CategoryIds.Contains(element.Category.Id);
    }

    private static bool HasCodeParameter(Document doc, Element element, Dictionary<ElementId, bool> typeCache)
    {
        if (element.LookupParameter(ClassifierParameters.Code) != null)
            return true;

        var typeId = element.GetTypeId();
        if (typeId == ElementId.InvalidElementId)
            return false;

        if (!typeCache.TryGetValue(typeId, out var has))
        {
            var type = doc.GetElement(typeId);
            has = type != null && type.LookupParameter(ClassifierParameters.Code) != null;
            typeCache[typeId] = has;
        }

        return has;
    }
}
