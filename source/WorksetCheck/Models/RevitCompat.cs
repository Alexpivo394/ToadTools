namespace WorksetCheck.Models;

internal static class RevitCompat
{
    public static long Value(this ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    public static bool Is(this Category? category, BuiltInCategory builtInCategory) =>
        category != null && category.Id.Value() == (long)builtInCategory;
}
