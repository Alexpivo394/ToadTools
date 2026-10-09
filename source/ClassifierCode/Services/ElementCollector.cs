using ClassifierCode.Models;

namespace ClassifierCode.Services;

/// <summary>
///     Собирает модельные элементы, у которых есть параметр кода (в экземпляре или типе), —
///     ровно то, что может попасть в спецификацию, независимо от категории.
/// </summary>
public sealed class ElementCollector
{
    // Обобщённые модели и осевые линии в проекте не используются и в спецификации не попадают — не трогаем.
    private static readonly ElementId CatGenericModel = new(BuiltInCategory.OST_GenericModel);
    private static readonly ElementId CatCenterLines = new(BuiltInCategory.OST_CenterLines);

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
            candidates = collector.WhereElementIsNotElementType().ToElements();
        }

        var typeHasParameter = new Dictionary<ElementId, bool>();
        return candidates
            .Where(e => e != null && IsCandidate(e) && HasCodeParameter(doc, e, typeHasParameter))
            .Select(e => e!)
            .ToList();
    }

    private static bool IsExcludedCategory(Category category)
    {
        return category.Id == CatGenericModel || category.Id == CatCenterLines ||
               string.Equals(category.Name, "Осевые линии", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCandidate(Element element)
    {
        return element is not ElementType &&
               !element.ViewSpecific &&
               element.Category != null &&
               element.Category.CategoryType == CategoryType.Model &&
               !IsExcludedCategory(element.Category) &&
               element is not SpatialElement &&
               element is not MEPSystem &&
               element is not Group &&
               element is not AssemblyInstance;
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
