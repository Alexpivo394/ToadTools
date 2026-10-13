namespace SmartTags.Services;

/// <summary>Различия Revit API между версиями 2020–2027.</summary>
internal static class RevitCompat
{
    public static long IdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    public static BuiltInCategory CategoryOf(Element element)
    {
        var category = element.Category;
        if (category is null) return BuiltInCategory.INVALID;

#if REVIT2023_OR_GREATER
        return category.BuiltInCategory;
#else
        var value = category.Id.IntegerValue;
        return value < 0 ? (BuiltInCategory)value : BuiltInCategory.INVALID;
#endif
    }

    public static bool Is(Element element, BuiltInCategory category) => CategoryOf(element) == category;
}
