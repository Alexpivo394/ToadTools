namespace SmartTags.Services;

/// <summary>
///     Какие категории умеем маркировать и какой категорией марок. Категории заданы именами и
///     разбираются в рантайме: часть из них есть не во всех версиях Revit.
/// </summary>
public static class TagCategoryMap
{
    private static readonly (string Element, string Tag)[] Pairs =
    [
        ("OST_DuctCurves", "OST_DuctTags"),
        ("OST_FlexDuctCurves", "OST_FlexDuctTags"),
        ("OST_DuctFitting", "OST_DuctFittingTags"),
        ("OST_DuctAccessory", "OST_DuctAccessoryTags"),
        ("OST_DuctTerminal", "OST_DuctTerminalTags"),
        ("OST_DuctInsulations", "OST_DuctInsulationsTags"),
        ("OST_DuctLinings", "OST_DuctLiningsTags"),
        ("OST_MechanicalEquipment", "OST_MechanicalEquipmentTags"),
        ("OST_PipeCurves", "OST_PipeTags"),
        ("OST_FlexPipeCurves", "OST_FlexPipeTags"),
        ("OST_PipeFitting", "OST_PipeFittingTags"),
        ("OST_PipeAccessory", "OST_PipeAccessoryTags"),
        ("OST_PipeInsulations", "OST_PipeInsulationsTags"),
        ("OST_PlumbingFixtures", "OST_PlumbingFixtureTags"),
        ("OST_Sprinklers", "OST_SprinklerTags"),
        ("OST_CableTray", "OST_CableTrayTags"),
        ("OST_CableTrayFitting", "OST_CableTrayFittingTags"),
        ("OST_Conduit", "OST_ConduitTags"),
        ("OST_ConduitFitting", "OST_ConduitFittingTags"),
        ("OST_ElectricalEquipment", "OST_ElectricalEquipmentTags"),
        ("OST_ElectricalFixtures", "OST_ElectricalFixtureTags"),
        ("OST_LightingFixtures", "OST_LightingFixtureTags"),
        ("OST_LightingDevices", "OST_LightingDeviceTags"),
        ("OST_DataDevices", "OST_DataDeviceTags"),
        ("OST_FireAlarmDevices", "OST_FireAlarmDeviceTags"),
        ("OST_CommunicationDevices", "OST_CommunicationDeviceTags"),
        ("OST_SecurityDevices", "OST_SecurityDeviceTags"),
        ("OST_NurseCallDevices", "OST_NurseCallDeviceTags"),
        ("OST_TelephoneDevices", "OST_TelephoneDeviceTags"),
        ("OST_GenericModel", "OST_GenericModelTags"),
        ("OST_SpecialityEquipment", "OST_SpecialityEquipmentTags"),
        ("OST_Furniture", "OST_FurnitureTags"),
        ("OST_Casework", "OST_CaseworkTags"),
        ("OST_Walls", "OST_WallTags"),
        ("OST_Doors", "OST_DoorTags"),
        ("OST_Windows", "OST_WindowTags"),
        ("OST_Floors", "OST_FloorTags"),
        ("OST_Ceilings", "OST_CeilingTags"),
        ("OST_StructuralFraming", "OST_StructuralFramingTags"),
        ("OST_StructuralColumns", "OST_StructuralColumnTags"),
        ("OST_Columns", "OST_ColumnTags")
    ];

    /// <summary>Инженерные категории: их элементы — препятствия для текста и выносок.</summary>
    private static readonly string[] ObstacleNames =
    [
        "OST_DuctCurves", "OST_FlexDuctCurves", "OST_DuctFitting", "OST_DuctAccessory", "OST_DuctTerminal",
        "OST_DuctInsulations", "OST_DuctLinings", "OST_MechanicalEquipment", "OST_PlaceHolderDucts",
        "OST_PipeCurves", "OST_FlexPipeCurves", "OST_PipeFitting", "OST_PipeAccessory", "OST_PipeInsulations",
        "OST_PlumbingFixtures", "OST_Sprinklers", "OST_PlaceHolderPipes", "OST_CableTray", "OST_CableTrayFitting",
        "OST_Conduit", "OST_ConduitFitting", "OST_ElectricalEquipment", "OST_ElectricalFixtures",
        "OST_LightingFixtures", "OST_LightingDevices", "OST_DataDevices", "OST_FireAlarmDevices",
        "OST_CommunicationDevices", "OST_SecurityDevices", "OST_NurseCallDevices", "OST_TelephoneDevices",
        "OST_GenericModel", "OST_SpecialityEquipment", "OST_MechanicalControlDevices", "OST_PlumbingEquipment"
    ];

    private static readonly HashSet<BuiltInCategory> LinearCategories =
    [
        BuiltInCategory.OST_DuctCurves,
        BuiltInCategory.OST_FlexDuctCurves,
        BuiltInCategory.OST_PipeCurves,
        BuiltInCategory.OST_FlexPipeCurves,
        BuiltInCategory.OST_CableTray,
        BuiltInCategory.OST_Conduit
    ];

    private static readonly Dictionary<BuiltInCategory, BuiltInCategory> TagByElement = Pairs
        .Select(pair => (Element: Parse(pair.Element), Tag: Parse(pair.Tag)))
        .Where(pair => pair.Element != BuiltInCategory.INVALID && pair.Tag != BuiltInCategory.INVALID)
        .ToDictionary(pair => pair.Element, pair => pair.Tag);

    public static IReadOnlyCollection<BuiltInCategory> TaggableCategories => TagByElement.Keys;

    public static IReadOnlyList<BuiltInCategory> ObstacleCategories { get; } = ObstacleNames
        .Select(Parse)
        .Where(category => category != BuiltInCategory.INVALID)
        .ToList();

    public static BuiltInCategory MultiCategoryTags { get; } = Parse("OST_MultiCategoryTags");

    public static bool IsTaggable(BuiltInCategory category) => TagByElement.ContainsKey(category);

    public static bool IsLinear(BuiltInCategory category) => LinearCategories.Contains(category);

    public static BuiltInCategory TagCategoryOf(BuiltInCategory category)
    {
        return TagByElement.TryGetValue(category, out var tag) ? tag : BuiltInCategory.INVALID;
    }

    private static BuiltInCategory Parse(string name)
    {
        return Enum.TryParse(name, out BuiltInCategory category) && Enum.IsDefined(typeof(BuiltInCategory), category)
            ? category
            : BuiltInCategory.INVALID;
    }
}
