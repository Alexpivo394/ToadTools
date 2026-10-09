using ClassifierCode.Models;

namespace ClassifierCode.Dictionaries;

/// <summary>
/// Классификатор "Пионер. Донской" (Пионер Классификатор_параметризация Донской.xlsx)
/// и правила подбора позиции, если в разделе нет точной роли.
/// </summary>
public static class ClassifierTable
{
    private static readonly Dictionary<Discipline, Dictionary<Role, ClassifierItem>> Items =
        new Dictionary<Discipline, Dictionary<Role, ClassifierItem>>();

    private static readonly Dictionary<Discipline, ClassifierItem> Sections =
        new Dictionary<Discipline, ClassifierItem>();

    // Если роли нет в разделе — пробуем по очереди эти замены.
    private static readonly Dictionary<Role, Role[]> Fallbacks = new Dictionary<Role, Role[]>
    {
        // В ПТ своей позиции для изоляции нет — относим к трубам раздела.
        { Role.Insulation, new[] { Role.FireThermalProtection, Role.PipeGroup } },
        { Role.FireThermalProtection, new[] { Role.Insulation } },
        { Role.Trap, new[] { Role.Funnel } },
        { Role.Funnel, new[] { Role.Trap } },
        { Role.Sprinkler, new[] { Role.Nozzle } },
        { Role.Nozzle, new[] { Role.Sprinkler } },
        { Role.Kip, new[] { Role.Armature } },
        { Role.Collector, new[] { Role.Equipment } },
        { Role.Mixing, new[] { Role.Equipment } },
        { Role.PumpAutomation, new[] { Role.PowderAutomation, Role.Equipment } },
        { Role.Equipment, new[] { Role.PowderModule } },
        { Role.Terminal, new[] { Role.Equipment } },
        { Role.HeatCurtain, new[] { Role.Terminal, Role.Equipment } },
        { Role.HeatingDevice, new[] { Role.Terminal, Role.Equipment } },
        { Role.TempHeatingDevice, new[] { Role.HeatingDevice } },
        { Role.WindowInlet, new[] { Role.Grille } },
        { Role.FireCabinet, new[] { Role.Equipment } },
        { Role.PipeGroup, new[] { Role.DuctGroup } },
        { Role.DuctGroup, new[] { Role.PipeGroup } },
        { Role.CableTray, new[] { Role.CableSupport } },
        { Role.CableTrayFitting, new[] { Role.CableSupport } }
    };

    static ClassifierTable()
    {
        Section(Discipline.Water, "04.05.01.01", "Комплекс работ по монтажу водопровода");
        Add(Discipline.Water, Role.Equipment, "04.05.01.01.01", "Оборудование");
        Add(Discipline.Water, Role.PipeGroup, "04.05.01.01.02", "Трубы");
        Add(Discipline.Water, Role.Pipe, "04.05.01.01.02.01", "Прямые участки");
        Add(Discipline.Water, Role.PipeFitting, "04.05.01.01.02.02", "Фитинги");
        Add(Discipline.Water, Role.Armature, "04.05.01.01.03", "Арматура");
        Add(Discipline.Water, Role.Kip, "04.05.01.01.04", "Контрольно - измерительные приборы");
        Add(Discipline.Water, Role.Collector, "04.05.01.01.05", "Распределительно - коллекторные узлы");
        Add(Discipline.Water, Role.Mixing, "04.05.01.01.06", "Смесительное оборудование");
        Add(Discipline.Water, Role.Insulation, "04.05.01.01.07", "Теплоизоляция");

        Section(Discipline.Sewer, "04.05.01.02", "Комплекс работ по монтажу канализации");
        Add(Discipline.Sewer, Role.Equipment, "04.05.01.02.01", "Оборудование");
        Add(Discipline.Sewer, Role.PipeGroup, "04.05.01.02.02", "Трубы");
        Add(Discipline.Sewer, Role.Pipe, "04.05.01.02.02.01", "Прямые участки");
        Add(Discipline.Sewer, Role.PipeFitting, "04.05.01.02.02.02", "Фитинги");
        Add(Discipline.Sewer, Role.Armature, "04.05.01.02.03", "Арматура");
        Add(Discipline.Sewer, Role.Trap, "04.05.01.02.04", "Трапы");
        Add(Discipline.Sewer, Role.SanitaryFixture, "04.05.01.02.05", "Сантехнические приборы");
        Add(Discipline.Sewer, Role.Insulation, "04.05.01.02.06", "Теплоизоляция");

        Section(Discipline.Storm, "04.05.01.03", "Комплекс работ по монтажу ливневой канализации");
        Add(Discipline.Storm, Role.PipeGroup, "04.05.01.03.01", "Трубы");
        Add(Discipline.Storm, Role.Pipe, "04.05.01.03.01.01", "Прямые участки");
        Add(Discipline.Storm, Role.PipeFitting, "04.05.01.03.01.02", "Фитинги");
        Add(Discipline.Storm, Role.Armature, "04.05.01.03.02", "Арматура");
        Add(Discipline.Storm, Role.Funnel, "04.05.01.03.03", "Воронки и трапы");
        Add(Discipline.Storm, Role.Insulation, "04.05.01.03.04", "Теплоизоляция");

        Section(Discipline.Fire, "04.05.01.04", "Автоматическое сприклерное пожаротушение и противопожарный водопроод");
        Add(Discipline.Fire, Role.Equipment, "04.05.01.04.01", "Оборудование");
        Add(Discipline.Fire, Role.PipeGroup, "04.05.01.04.02", "Трубы");
        Add(Discipline.Fire, Role.Pipe, "04.05.01.04.02.01", "Прямые участки");
        Add(Discipline.Fire, Role.PipeFitting, "04.05.01.04.02.02", "Фитинги");
        Add(Discipline.Fire, Role.Armature, "04.05.01.04.03", "Арматура");
        Add(Discipline.Fire, Role.Kip, "04.05.01.04.04", "Контрольно - измерительные приборы");
        Add(Discipline.Fire, Role.Sprinkler, "04.05.01.04.05", "Оросители");
        Add(Discipline.Fire, Role.FireCabinet, "04.05.01.04.06", "Пожарные шкафы");
        Add(Discipline.Fire, Role.PumpAutomation, "04.05.01.04.07", "Автоматика насосов");

        Section(Discipline.Gas, "04.05.01.05", "Газовое пожаротушение");
        Add(Discipline.Gas, Role.Equipment, "04.05.01.05.01", "Оборудование");
        Add(Discipline.Gas, Role.PipeGroup, "04.05.01.05.02", "Трубы");
        Add(Discipline.Gas, Role.Pipe, "04.05.01.05.02.01", "Прямые участки");
        Add(Discipline.Gas, Role.PipeFitting, "04.05.01.05.02.02", "Фитинги");
        Add(Discipline.Gas, Role.Armature, "04.05.01.05.03", "Арматура");
        Add(Discipline.Gas, Role.Kip, "04.05.01.05.04", "Контрольно - измерительные приборы");
        Add(Discipline.Gas, Role.Nozzle, "04.05.01.05.05", "Распылители");
        Add(Discipline.Gas, Role.CableSupport, "04.05.01.05.06", "Кабеленесущие изделия");
        Add(Discipline.Gas, Role.Detector, "04.05.01.05.07", "Извещатели");

        Section(Discipline.Powder, "04.05.01.06", "Порошковое пожаротушение");
        Add(Discipline.Powder, Role.PowderModule, "04.05.01.06.01", "Модули порошковые");
        Add(Discipline.Powder, Role.PowderAutomation, "04.05.01.06.02", "Автоматика порошкового пожаротушения");
        Add(Discipline.Powder, Role.Detector, "04.05.01.06.03", "Извещатели");
        Add(Discipline.Powder, Role.CableSupport, "04.05.01.06.04", "Кабеленесущие изделия");

        Section(Discipline.Heating, "04.05.01.07", "Устройство внутренних сетей отопления");
        Add(Discipline.Heating, Role.HeatingDevice, "04.05.01.07.01", "Отопительные приборы");
        Add(Discipline.Heating, Role.TempHeatingDevice, "04.05.01.07.02", "Временные отопительные приборы");
        Add(Discipline.Heating, Role.Equipment, "04.05.01.07.03", "Оборудование");
        Add(Discipline.Heating, Role.PipeGroup, "04.05.01.07.04", "Трубы");
        Add(Discipline.Heating, Role.Pipe, "04.05.01.07.04.01", "Прямые участки");
        Add(Discipline.Heating, Role.PipeFitting, "04.05.01.07.04.02", "Фитинги");
        Add(Discipline.Heating, Role.Armature, "04.05.01.07.05", "Арматура");
        Add(Discipline.Heating, Role.Kip, "04.05.01.07.06", "Контрольно - измерительные приборы");
        Add(Discipline.Heating, Role.Collector, "04.05.01.07.07", "Распределительно - коллекторные узлы");
        Add(Discipline.Heating, Role.Insulation, "04.05.01.07.08", "Теплоизоляция");

        Section(Discipline.HeatSupply, "04.05.01.08", "Устройство внутренних сетей теплоснабжения");
        Add(Discipline.HeatSupply, Role.Terminal, "04.05.01.08.01", "Оконечные устройства");
        Add(Discipline.HeatSupply, Role.Equipment, "04.05.01.08.02", "Оборудование");
        Add(Discipline.HeatSupply, Role.PipeGroup, "04.05.01.08.03", "Трубы");
        Add(Discipline.HeatSupply, Role.Pipe, "04.05.01.08.03.01", "Прямые участки");
        Add(Discipline.HeatSupply, Role.PipeFitting, "04.05.01.08.03.02", "Фитинги");
        Add(Discipline.HeatSupply, Role.Armature, "04.05.01.08.04", "Арматура");
        Add(Discipline.HeatSupply, Role.Kip, "04.05.01.08.05", "Контрольно - измерительные приборы");
        Add(Discipline.HeatSupply, Role.Collector, "04.05.01.08.06", "Распределительно - коллекторные узлы");
        Add(Discipline.HeatSupply, Role.Insulation, "04.05.01.08.07", "Теплоизоляция");

        Section(Discipline.Vent, "04.05.01.09", "Работы по монтажу общеобменной вентиляции");
        Add(Discipline.Vent, Role.Equipment, "04.05.01.09.01", "Оборудование");
        Add(Discipline.Vent, Role.DuctGroup, "04.05.01.09.02", "Воздуховоды");
        Add(Discipline.Vent, Role.Duct, "04.05.01.09.02.01", "Прямые участки");
        Add(Discipline.Vent, Role.DuctFitting, "04.05.01.09.02.02", "Фасонные элементы");
        Add(Discipline.Vent, Role.Armature, "04.05.01.09.03", "Арматура");
        Add(Discipline.Vent, Role.HeatCurtain, "04.05.01.09.04", "Тепловые завесы");
        Add(Discipline.Vent, Role.WindowInlet, "04.05.01.09.05", "Приточные оконные устройства");
        Add(Discipline.Vent, Role.Grille, "04.05.01.09.06", "Решётки, диффузоры");
        Add(Discipline.Vent, Role.FireThermalProtection, "04.05.01.09.07", "Тепло - огнезащита");

        Section(Discipline.Smoke, "04.05.01.10", "Работы по монтажу противодымной вентиляции");
        Add(Discipline.Smoke, Role.Equipment, "04.05.01.10.01", "Оборудование");
        Add(Discipline.Smoke, Role.DuctGroup, "04.05.01.10.02", "Воздуховоды");
        Add(Discipline.Smoke, Role.Duct, "04.05.01.10.02.01", "Прямые участки");
        Add(Discipline.Smoke, Role.DuctFitting, "04.05.01.10.02.02", "Фасонные элементы");
        Add(Discipline.Smoke, Role.FireThermalProtection, "04.05.01.10.03", "Тепло - огнезащита");
        Add(Discipline.Smoke, Role.Armature, "04.05.01.10.04", "Арматура");
        Add(Discipline.Smoke, Role.Grille, "04.05.01.10.05", "Решётки");

        Section(Discipline.Cooling, "04.05.01.11", "Работы по монтажу системы холодоснабжения");
        Add(Discipline.Cooling, Role.Equipment, "04.05.01.11.01", "Оборудование");
        Add(Discipline.Cooling, Role.PipeGroup, "04.05.01.11.02", "Трубы");
        Add(Discipline.Cooling, Role.Pipe, "04.05.01.11.02.01", "Прямые участки");
        Add(Discipline.Cooling, Role.PipeFitting, "04.05.01.11.02.02", "Фитинги");
        Add(Discipline.Cooling, Role.Armature, "04.05.01.11.03", "Арматура");
        Add(Discipline.Cooling, Role.Kip, "04.05.01.11.04", "Контрольно - измерительные приборы");
        Add(Discipline.Cooling, Role.Insulation, "04.05.01.11.05", "Теплоизоляция");
        Add(Discipline.Cooling, Role.Coolant, "04.05.01.11.06", "Хладоноситель");
        Add(Discipline.Cooling, Role.DuctGroup, "04.05.01.11.08", "Воздуховоды");
        Add(Discipline.Cooling, Role.Duct, "04.05.01.11.08.01", "Прямые участки");
        Add(Discipline.Cooling, Role.DuctFitting, "04.05.01.11.08.02", "Фасонные элементы");
        Add(Discipline.Cooling, Role.Grille, "04.05.01.11.09", "Решётки, диффузоры");

        Section(Discipline.Electrical, "04.05.02", "Работы по монтажу систем ЭОМ");
        Add(Discipline.Electrical, Role.Switchgear, "04.05.02.01", "Электрощитовое оборудование");
        Add(Discipline.Electrical, Role.PowerPanel, "04.05.02.02", "Щиты силовые распределительные");
        Add(Discipline.Electrical, Role.Uerm, "04.05.02.03", "Устройство УЭРМ (В)");
        Add(Discipline.Electrical, Role.CableSupport, "04.05.02.04", "Кабеленесущие изделия");
        Add(Discipline.Electrical, Role.CableTray, "04.05.02.04.01", "Прямые участки");
        Add(Discipline.Electrical, Role.CableTrayFitting, "04.05.02.04.02", "Фасонные элементы");
        Add(Discipline.Electrical, Role.Cable, "04.05.02.05", "Кабельные изделия и провода");
        Add(Discipline.Electrical, Role.Grounding, "04.05.02.06", "Молниезащита и заземление");
        Add(Discipline.Electrical, Role.FireproofBox, "04.05.02.07", "Короба огнезащитные");
        Add(Discipline.Electrical, Role.Busway, "04.05.02.08", "Шинопроводы");
        Add(Discipline.Electrical, Role.Lighting, "04.05.02.09", "Электроосветительное оборудование");
        Add(Discipline.Electrical, Role.WiringDevice, "04.05.02.10", "Электроустановочные и электромонтажные изделия");
        Add(Discipline.Electrical, Role.CablePenetration, "04.05.02.12", "Устройство кабельных проходок ЭОМ");
    }

    private static void Section(Discipline discipline, string code, string description)
    {
        Sections[discipline] = new ClassifierItem(code, description);
        Items[discipline] = new Dictionary<Role, ClassifierItem>();
    }

    private static void Add(Discipline discipline, Role role, string code, string description)
    {
        Items[discipline][role] = new ClassifierItem(code, description);
    }

    public static ClassifierItem? GetSection(Discipline discipline)
    {
        return Sections.TryGetValue(discipline, out var item) ? item : null;
    }

    /// <summary>Позиция классификатора для пары раздел/роль с учётом замен. null — позиции нет.</summary>
    public static ClassifierItem? Find(Discipline discipline, Role role)
    {
        if (!Items.TryGetValue(discipline, out var roles))
            return null;

        if (roles.TryGetValue(role, out var item))
            return item;

        if (Fallbacks.TryGetValue(role, out var alternatives))
        {
            foreach (var alternative in alternatives)
            {
                if (roles.TryGetValue(alternative, out item))
                    return item;
            }
        }
        return null;
    }
}
