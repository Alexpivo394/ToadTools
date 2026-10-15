using Autodesk.Revit.DB.Mechanical;
using SmartTags.Models;

namespace SmartTags.Services;

/// <summary>Ищет на виде категории, которые можно промаркировать, и загруженные для них типы марок.</summary>
public sealed class CategoryScanner(Document document, View view)
{
    private readonly Dictionary<BuiltInCategory, List<TagTypeOption>> _tagTypes = new();

    /// <summary>Подсказка, почему список категорий пуст.</summary>
    public string? EmptyHint { get; private set; }

    /// <summary>Видимые элементы текущего проекта на виде, сгруппированные по категориям.</summary>
    public List<TaggableCategory> ScanView()
    {
        var elements = new FilteredElementCollector(document, view.Id)
            .WherePasses(new ElementMulticategoryFilter(TagCategoryMap.TaggableCategories.ToList()))
            .WhereElementIsNotElementType()
            .Where(IsVisible);

        var result = Build(elements);
        if (result.Count == 0) EmptyHint = ExplainEmptyView();
        return result;
    }

    /// <summary>Только выбранные элементы, которые видны на виде.</summary>
    public List<TaggableCategory> ScanSelection(ICollection<ElementId> selection)
    {
        if (selection.Count == 0) return [];

        var selected = new HashSet<ElementId>(selection);
        var elements = new FilteredElementCollector(document, view.Id)
            .WherePasses(new ElementMulticategoryFilter(TagCategoryMap.TaggableCategories.ToList()))
            .WhereElementIsNotElementType()
            .Where(element => selected.Contains(element.Id) && IsVisible(element));

        var result = Build(elements);
        if (result.Count == 0)
            EmptyHint = "Среди выбранных элементов нет труб, воздуховодов и других категорий, для которых в проект загружены марки.";
        return result;
    }

    /// <summary>
    ///     Выбор плюс изоляция и внутренняя изоляция выбранных труб, воздуховодов и фитингов: щелчок или рамка
    ///     выбирают сам элемент, а его изоляция — отдельный элемент, который в выбор не попадает.
    /// </summary>
    public ICollection<ElementId> WithInsulation(ICollection<ElementId> selection)
    {
        if (selection.Count == 0) return selection;

        var result = new HashSet<ElementId>(selection);
        foreach (var id in selection)
        {
            try
            {
                result.UnionWith(InsulationLiningBase.GetInsulationIds(document, id));
                result.UnionWith(InsulationLiningBase.GetLiningIds(document, id));
            }
            catch
            {
                // Элемент не может нести изоляцию (марка, аннотация и т. п.).
            }
        }

        return result;
    }

    private List<TaggableCategory> Build(IEnumerable<Element> elements)
    {
        var result = new List<TaggableCategory>();
        foreach (var group in elements.GroupBy(RevitCompat.CategoryOf))
        {
            var tagTypes = TagTypesFor(group.Key);
            if (tagTypes.Count == 0) continue;

            var name = Category.GetCategory(document, group.Key)?.Name ?? group.Key.ToString();
            if (group.Key != BuiltInCategory.OST_DuctCurves)
            {
                result.Add(new TaggableCategory(group.Key, name, group.Count(), DuctProfile.Any, tagTypes));
                continue;
            }

            // Воздуховоды — двумя строками: у круглых и прямоугольных обычно разные марки.
            var round = group.Count(element => ProfileOf(element) == DuctProfile.Round);
            var rectangular = group.Count() - round;
            if (round > 0)
                result.Add(new TaggableCategory(group.Key, name + " круглые", round, DuctProfile.Round, tagTypes));
            if (rectangular > 0)
                result.Add(new TaggableCategory(group.Key, name + " прямоугольные", rectangular, DuctProfile.Rectangular, tagTypes));
        }

        return result.OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static DuctProfile ProfileOf(Element element)
    {
        if (element is Duct { DuctType: { } ductType })
        {
            var shape = ductType.Shape;
            if (shape == ConnectorProfileType.Round) return DuctProfile.Round;
            if (shape is ConnectorProfileType.Rectangular or ConnectorProfileType.Oval) return DuctProfile.Rectangular;
        }

        if (element is not MEPCurve curve) return DuctProfile.Any;

        try
        {
            if (curve.Diameter > 1e-9) return DuctProfile.Round;
        }
        catch
        {
            // У прямоугольного сечения диаметра нет — обращение бросает исключение.
        }

        return DuctProfile.Rectangular;
    }

    /// <summary>Типы марок категории элемента плюс мультикатегорийные марки.</summary>
    private List<TagTypeOption> TagTypesFor(BuiltInCategory elementCategory)
    {
        var own = TagTypesOf(TagCategoryMap.TagCategoryOf(elementCategory), false);
        var multi = TagTypesOf(TagCategoryMap.MultiCategoryTags, true);
        return own.Concat(multi).ToList();
    }

    private List<TagTypeOption> TagTypesOf(BuiltInCategory tagCategory, bool isMulti)
    {
        if (tagCategory == BuiltInCategory.INVALID) return [];
        if (_tagTypes.TryGetValue(tagCategory, out var cached)) return cached;

        var types = new FilteredElementCollector(document)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(tagCategory)
            .Cast<FamilySymbol>()
            .Select(symbol => new TagTypeOption(symbol.Id, symbol.FamilyName, symbol.Name, isMulti))
            .OrderBy(type => type.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _tagTypes[tagCategory] = types;
        return types;
    }

    private bool IsVisible(Element element)
    {
        try
        {
            return !element.IsHidden(view);
        }
        catch
        {
            return true;
        }
    }

    private string ExplainEmptyView()
    {
        var hasElements = new FilteredElementCollector(document, view.Id)
            .WherePasses(new ElementMulticategoryFilter(TagCategoryMap.TaggableCategories.ToList()))
            .WhereElementIsNotElementType()
            .Any();
        if (hasElements)
            return "На виде есть элементы, но для их категорий в проект не загружено ни одного семейства марок.\n\n" +
                   "Загрузите марку нужной категории (например, «Марка трубопровода») и запустите плагин снова.";

        var hasLinks = new FilteredElementCollector(document, view.Id)
            .OfCategory(BuiltInCategory.OST_RvtLinks)
            .Any();
        if (hasLinks)
            return "На виде видны только элементы из связанных файлов, а плагин маркирует элементы текущего проекта.\n\n" +
                   "Откройте исходный файл связи.";

        return "На виде не найдено видимых элементов, которые умеет маркировать плагин (трубы, воздуховоды, " +
               "оборудование и т. д.).\n\nПроверьте «Видимость/графику» вида.";
    }
}
