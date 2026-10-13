namespace SmartTags.Models;

/// <summary>Тип марки, загруженный в проект.</summary>
public sealed class TagTypeOption(ElementId id, string familyName, string typeName, bool isMultiCategory)
{
    public ElementId Id { get; } = id;
    public string FamilyName { get; } = familyName;
    public string TypeName { get; } = typeName;
    public bool IsMultiCategory { get; } = isMultiCategory;

    public string DisplayName => (IsMultiCategory ? "[Мульти] " : string.Empty) + FamilyName + " : " + TypeName;

    public override string ToString() => DisplayName;
}

/// <summary>Категория элементов на виде, для которой в проекте есть марки.</summary>
public sealed class TaggableCategory(
    BuiltInCategory category,
    string name,
    int elementCount,
    DuctProfile ductProfile,
    IReadOnlyList<TagTypeOption> tagTypes)
{
    public BuiltInCategory Category { get; } = category;
    public string Name { get; } = name;
    public int ElementCount { get; } = elementCount;

    /// <summary>Воздуховоды разделены по форме сечения (у круглых и прямоугольных обычно разные марки).</summary>
    public DuctProfile DuctProfile { get; } = ductProfile;

    public IReadOnlyList<TagTypeOption> TagTypes { get; } = tagTypes;

    /// <summary>Линейные элементы (трубы, воздуховоды, лотки) маркируются участками, а не поштучно.</summary>
    public bool IsLinear => Services.TagCategoryMap.IsLinear(Category);

    public bool IsDucts => Category == BuiltInCategory.OST_DuctCurves;
}

/// <summary>Выбор пользователя: какую категорию маркировать и каким типом марки.</summary>
public sealed record CategoryChoice(
    BuiltInCategory Category,
    string Name,
    DuctProfile DuctProfile,
    TagTypeOption TagType)
{
    public bool IsLinear => Services.TagCategoryMap.IsLinear(Category);
}
