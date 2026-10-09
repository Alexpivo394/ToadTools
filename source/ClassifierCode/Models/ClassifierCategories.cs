namespace ClassifierCode.Models;

/// <summary>
///     Категории, к которым привязываются параметры классификатора и элементы которых обрабатывает плагин.
/// </summary>
public static class ClassifierCategories
{
    public static IReadOnlyList<BuiltInCategory> All { get; } =
    [
        // Трубопроводы
        BuiltInCategory.OST_PipeCurves,
        BuiltInCategory.OST_FlexPipeCurves,
        BuiltInCategory.OST_PipeFitting,
        BuiltInCategory.OST_PipeAccessory,
        BuiltInCategory.OST_PipeInsulations,
        BuiltInCategory.OST_PlumbingFixtures,
        BuiltInCategory.OST_Sprinklers,

        // Воздуховоды
        BuiltInCategory.OST_DuctCurves,
        BuiltInCategory.OST_FlexDuctCurves,
        BuiltInCategory.OST_DuctFitting,
        BuiltInCategory.OST_DuctAccessory,
        BuiltInCategory.OST_DuctTerminal,
        BuiltInCategory.OST_DuctInsulations,
        BuiltInCategory.OST_DuctLinings,

        // Оборудование
        BuiltInCategory.OST_MechanicalEquipment,
        BuiltInCategory.OST_ElectricalEquipment,
        BuiltInCategory.OST_Casework,

        // Кабеленесущие системы
        BuiltInCategory.OST_CableTray,
        BuiltInCategory.OST_CableTrayFitting,
        BuiltInCategory.OST_Conduit,
        BuiltInCategory.OST_ConduitFitting,

        // Электрика и освещение
        BuiltInCategory.OST_ElectricalFixtures,
        BuiltInCategory.OST_LightingFixtures,
        BuiltInCategory.OST_LightingDevices,

        // Слаботочные системы
        BuiltInCategory.OST_FireAlarmDevices,
        BuiltInCategory.OST_SecurityDevices,
        BuiltInCategory.OST_NurseCallDevices,
        BuiltInCategory.OST_TelephoneDevices,
        BuiltInCategory.OST_CommunicationDevices
    ];
}
