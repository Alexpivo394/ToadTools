namespace Collisions.Core;

/// <summary>
///     Русские имена категорий Revit (BuiltInCategory) — те же, что в веб-интерфейсе ToadFlow (<c>web/src/lib/categories.ts</c>).
///     Незнакомая категория показывается как есть.
/// </summary>
public static class CategoryNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["OST_Walls"] = "Стены",
        ["OST_Floors"] = "Перекрытия",
        ["OST_Roofs"] = "Крыши",
        ["OST_Ceilings"] = "Потолки",
        ["OST_Doors"] = "Двери",
        ["OST_Windows"] = "Окна",
        ["OST_Stairs"] = "Лестницы",
        ["OST_StairsRailing"] = "Ограждения",
        ["OST_Railings"] = "Ограждения",
        ["OST_Ramps"] = "Пандусы",
        ["OST_Columns"] = "Колонны",
        ["OST_StructuralColumns"] = "Несущие колонны",
        ["OST_StructuralFraming"] = "Каркас несущий",
        ["OST_StructuralFoundation"] = "Фундамент несущей конструкции",
        ["OST_StructuralStiffener"] = "Элементы жёсткости",
        ["OST_Rebar"] = "Несущая арматура",
        ["OST_GenericModel"] = "Обобщённые модели",
        ["OST_Mass"] = "Формы",
        ["OST_Furniture"] = "Мебель",
        ["OST_FurnitureSystems"] = "Системы мебели",
        ["OST_Casework"] = "Шкафы",
        ["OST_SpecialityEquipment"] = "Специальное оборудование",
        ["OST_PlumbingFixtures"] = "Сантехнические приборы",
        ["OST_Parking"] = "Парковка",
        ["OST_Planting"] = "Озеленение",
        ["OST_Site"] = "Генплан",
        ["OST_Topography"] = "Топография",
        ["OST_CurtainWallPanels"] = "Панели витража",
        ["OST_CurtainWallMullions"] = "Импосты витража",
        ["OST_CurtaSystem"] = "Витражные системы",
        ["OST_Entourage"] = "Антураж",
        ["OST_Rooms"] = "Помещения",
        ["OST_PipeCurves"] = "Трубы",
        ["OST_PipeFitting"] = "Соединительные детали трубопроводов",
        ["OST_PipeAccessory"] = "Арматура трубопроводов",
        ["OST_PipeInsulations"] = "Материалы изоляции труб",
        ["OST_FlexPipeCurves"] = "Гибкие трубы",
        ["OST_DuctCurves"] = "Воздуховоды",
        ["OST_DuctFitting"] = "Соединительные детали воздуховодов",
        ["OST_DuctAccessory"] = "Арматура воздуховодов",
        ["OST_DuctInsulations"] = "Материалы изоляции воздуховодов",
        ["OST_DuctLinings"] = "Внутренняя изоляция воздуховодов",
        ["OST_FlexDuctCurves"] = "Гибкие воздуховоды",
        ["OST_DuctTerminal"] = "Воздухораспределители",
        ["OST_MechanicalEquipment"] = "Оборудование",
        ["OST_Sprinklers"] = "Спринклеры",
        ["OST_CableTray"] = "Кабельные лотки",
        ["OST_CableTrayFitting"] = "Соединительные детали кабельных лотков",
        ["OST_Conduit"] = "Короба",
        ["OST_ConduitFitting"] = "Соединительные детали коробов",
        ["OST_ElectricalEquipment"] = "Электрооборудование",
        ["OST_ElectricalFixtures"] = "Электрические приборы",
        ["OST_LightingFixtures"] = "Осветительные приборы",
        ["OST_LightingDevices"] = "Выключатели",
        ["OST_CommunicationDevices"] = "Устройства связи",
        ["OST_DataDevices"] = "Устройства передачи данных",
        ["OST_FireAlarmDevices"] = "Пожарная сигнализация",
        ["OST_SecurityDevices"] = "Охранные устройства",
        ["OST_NurseCallDevices"] = "Системы вызова медсестры",
        ["OST_TelephoneDevices"] = "Телефонные устройства",
        ["OST_Wire"] = "Провода",
        ["OST_MEPSpaces"] = "Пространства",
        ["OST_Parts"] = "Части",
        ["OST_Assemblies"] = "Сборки",
        ["OST_DetailComponents"] = "Элементы узлов"
    };

    /// <summary>«OST_Walls» → «Стены»; незнакомая — как есть.</summary>
    public static string Label(string category) => Names.TryGetValue(category, out var name) ? name : category;
}
