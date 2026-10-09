using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB.Mechanical;
using ClassifierCode.Dictionaries;
using ClassifierCode.Models;

namespace ClassifierCode.Services;

/// <summary>
/// Определяет раздел (по системе, родительскому семейству, наименованию, шифру документа)
/// и роль элемента (по категории и наименованию), затем позицию классификатора.
/// </summary>
public sealed class ElementClassifier
{
    private enum NetworkDomain
    {
        Pipe,
        Air,
        Other
    }

    private static readonly ElementId CatPipe = Id(BuiltInCategory.OST_PipeCurves);
    private static readonly ElementId CatFlexPipe = Id(BuiltInCategory.OST_FlexPipeCurves);
    private static readonly ElementId CatPipeFitting = Id(BuiltInCategory.OST_PipeFitting);
    private static readonly ElementId CatPipeAccessory = Id(BuiltInCategory.OST_PipeAccessory);
    private static readonly ElementId CatPipeInsulation = Id(BuiltInCategory.OST_PipeInsulations);
    private static readonly ElementId CatSprinkler = Id(BuiltInCategory.OST_Sprinklers);
    private static readonly ElementId CatPlumbing = Id(BuiltInCategory.OST_PlumbingFixtures);
    private static readonly ElementId CatMechEquipment = Id(BuiltInCategory.OST_MechanicalEquipment);
    private static readonly ElementId CatDuct = Id(BuiltInCategory.OST_DuctCurves);
    private static readonly ElementId CatFlexDuct = Id(BuiltInCategory.OST_FlexDuctCurves);
    private static readonly ElementId CatDuctFitting = Id(BuiltInCategory.OST_DuctFitting);
    private static readonly ElementId CatDuctAccessory = Id(BuiltInCategory.OST_DuctAccessory);
    private static readonly ElementId CatDuctTerminal = Id(BuiltInCategory.OST_DuctTerminal);
    private static readonly ElementId CatDuctInsulation = Id(BuiltInCategory.OST_DuctInsulations);
    private static readonly ElementId CatDuctLining = Id(BuiltInCategory.OST_DuctLinings);
    private static readonly ElementId CatFireAlarm = Id(BuiltInCategory.OST_FireAlarmDevices);
    private static readonly ElementId CatElecEquipment = Id(BuiltInCategory.OST_ElectricalEquipment);
    private static readonly ElementId CatCableTray = Id(BuiltInCategory.OST_CableTray);
    private static readonly ElementId CatCableTrayFitting = Id(BuiltInCategory.OST_CableTrayFitting);
    private static readonly ElementId CatConduit = Id(BuiltInCategory.OST_Conduit);
    private static readonly ElementId CatConduitFitting = Id(BuiltInCategory.OST_ConduitFitting);
    private static readonly ElementId CatElecFixture = Id(BuiltInCategory.OST_ElectricalFixtures);
    private static readonly ElementId CatLightingFixture = Id(BuiltInCategory.OST_LightingFixtures);
    private static readonly ElementId CatLightingDevice = Id(BuiltInCategory.OST_LightingDevices);
    private static readonly ElementId CatWire = Id(BuiltInCategory.OST_Wire);

    private readonly Document _doc;
    private readonly Discipline _documentDiscipline;
    private readonly Discipline _fixedSection;
    private readonly Dictionary<ElementId, Discipline> _disciplineCache = new Dictionary<ElementId, Discipline>();
    private readonly Dictionary<ElementId, Role> _roleCache = new Dictionary<ElementId, Role>();

    /// <param name="fixedSection">
    /// Раздел модели, заданный пользователем. Имена систем на раздел не влияют:
    /// Fire — модель ПТ (В1 на вводе в насосную остаётся ПТ; исключение — явные АГПТ / АППТ);
    /// Cooling — модель кондиционирования (воздуховоды канальных блоков в системах "П"/"В" остаются 04.05.01.11).
    /// None — раздел определяется по системам.
    /// </param>
    public ElementClassifier(Document doc, Discipline fixedSection)
    {
        _doc = doc;
        _fixedSection = fixedSection;
        _documentDiscipline = fixedSection != Discipline.None ? fixedSection : TextRules.MatchDocumentTitle(doc.Title);
    }

    private static ElementId Id(BuiltInCategory category)
    {
        return new ElementId(category);
    }

    public Classification Classify(Element element)
    {
        Classification result = new Classification();
        if ((_fixedSection == Discipline.Electrical || _fixedSection == Discipline.LowCurrent) && IsOpeningTask(element))
        {
            result.Skip = true;
            result.Reason = "задание на отверстие";
            return result;
        }
        if (_fixedSection == Discipline.LowCurrent)
            return ClassifyLowCurrent(element, result);

        result.Role = GetRole(element);
        if (result.Role == Role.None)
        {
            result.Reason = "не распознан тип элемента";
            return result;
        }

        Discipline discipline = GetDiscipline(element);
        result.Discipline = _fixedSection != Discipline.None ? discipline : ApplyRoleConstraints(result.Role, discipline);
        if (result.Discipline == Discipline.None)
        {
            result.Reason = "не определена система";
            return result;
        }

        result.Item = ClassifierTable.Find(result.Discipline, result.Role);
        if (result.Item == null)
            result.Reason = "нет позиции в разделе " + ClassifierTable.GetSection(result.Discipline)?.Code;
        return result;
    }

    // ================================ Роль ================================

    private Role GetRole(Element element)
    {
        Role role;
        if (_roleCache.TryGetValue(element.Id, out role))
            return role;

        role = ComputeRole(element);
        _roleCache[element.Id] = role;
        return role;
    }

    private Role ComputeRole(Element element)
    {
        if (_fixedSection == Discipline.Electrical)
            return ComputeElectricalRole(element);

        ElementId cat = element.Category != null ? element.Category.Id : ElementId.InvalidElementId;
        string text = GetElementText(element);
        NetworkDomain domain = GetDomain(element, null);
        // Неподключённые элементы (обобщённые модели, сетки, стаканы) в модели вентиляции считаем
        // относящимися к воздуховодам. В кондиционировании нельзя: там есть медь и ПП.
        bool air = domain == NetworkDomain.Air ||
                   (domain == NetworkDomain.Other && _fixedSection == Discipline.Vent);

        if (cat == CatPipe || cat == CatFlexPipe)
            return TextRules.Auxiliary.IsMatch(text) ? Role.PipeGroup : Role.Pipe;
        if (cat == CatPipeFitting)
            return Role.PipeFitting;
        if (cat == CatDuct || cat == CatFlexDuct)
            return Role.Duct;
        if (cat == CatDuctFitting)
            return Role.DuctFitting;
        if (cat == CatPipeInsulation || cat == CatDuctInsulation || cat == CatDuctLining)
            return TextRules.FireThermal.IsMatch(text) ? Role.FireThermalProtection : Role.Insulation;
        if (cat == CatSprinkler)
            return Role.Sprinkler;
        if (cat == CatFireAlarm)
            return Role.Detector;
        if (cat == CatCableTray || cat == CatCableTrayFitting || cat == CatConduit || cat == CatConduitFitting)
            return Role.CableSupport;
        if (cat == CatDuctTerminal)
            return TextRules.WindowInlet.IsMatch(text) ? Role.WindowInlet : Role.Grille;

        // Вложенный элемент комплекта пожарного крана (головка, рукав, ствол, клапан...) — в "Пожарные шкафы".
        FamilyInstance? instance = element as FamilyInstance;
        if (instance != null && instance.SuperComponent != null && GetRole(instance.SuperComponent) == Role.FireCabinet)
            return Role.FireCabinet;

        if (cat == CatMechEquipment)
        {
            // Оборудование может оказаться клапаном, щитом, смесительным узлом или разветвителем,
            // но не трубой/воздуховодом/изоляцией.
            Role equipmentRole = RoleByName(element, text, air, false);
            switch (equipmentRole)
            {
                case Role.None:
                case Role.Pipe:
                case Role.Duct:
                case Role.PipeGroup:
                case Role.DuctGroup:
                case Role.Insulation:
                case Role.FireThermalProtection:
                case Role.Coolant:
                    return Role.Equipment;
                case Role.DuctFitting:
                    return air && TextRules.Refnet.IsMatch(text) ? Role.PipeFitting : equipmentRole;
                default:
                    return equipmentRole;
            }
        }

        if (cat == CatPlumbing)
        {
            if (TextRules.FireCabinet.IsMatch(text)) return Role.FireCabinet;
            if (TextRules.Sprinkler.IsMatch(text)) return Role.Sprinkler;
            if (TextRules.Funnel.IsMatch(text)) return Role.Funnel;
            if (TextRules.Trap.IsMatch(text)) return Role.Trap;
            // Смесители и душевые лейки — водопровод "Смесительное оборудование", а не сантехприборы канализации.
            string primary = GetPrimaryName(element);
            if (TextRules.Mixing.IsMatch(primary.Length > 0 ? primary : text))
                return Role.Mixing;
            return Role.SanitaryFixture;
        }

        if (cat == CatElecEquipment)
        {
            if (TextRules.PowderModule.IsMatch(text)) return Role.PowderModule;
            if (TextRules.Detector.IsMatch(text)) return Role.Detector;
            return Role.PumpAutomation;
        }

        bool isMepCategory = cat == CatPipeAccessory || cat == CatDuctAccessory;
        Role byName = RoleByName(element, text, air, isMepCategory);
        if (byName != Role.None)
            return byName;

        // Арматура трубопроводов/воздуховодов без явных признаков — арматура.
        return isMepCategory ? Role.Armature : Role.None;
    }

    /// <summary>
    /// Сначала по ADSK_Наименование (главное слово названия стоит в начале),
    /// если там ничего не нашлось — по всей склейке семейство/тип/марка.
    /// </summary>
    private Role RoleByName(Element element, string fullText, bool air, bool isMepCategory)
    {
        Role role = TextRules.RoleByName(GetPrimaryName(element), air, isMepCategory);
        if (role != Role.None)
            return role;
        return TextRules.RoleByName(fullText, air, isMepCategory);
    }

    /// <summary>
    /// Модель ЭОМ (всё → 04.05.02). Лотки и короба — по категории; группирование "УЭРМ" решает сразу;
    /// затем ADSK_Наименование, семейство/тип, остальное группирование, родительское семейство, категория.
    /// </summary>
    private Role ComputeElectricalRole(Element element)
    {
        ElementId cat = element.Category != null ? element.Category.Id : ElementId.InvalidElementId;
        string text = GetElementText(element);

        if (cat == CatCableTray || cat == CatConduit)
            return TextRules.FireproofBox.IsMatch(text) ? Role.FireproofBox : Role.CableTray;
        if (cat == CatCableTrayFitting || cat == CatConduitFitting)
            return TextRules.FireproofBox.IsMatch(text) ? Role.FireproofBox : Role.CableTrayFitting;
        if (cat == CatWire)
            return Role.Cable;

        Role byGroup = TextRules.ElectricalRoleByGroup(GetTypeOrInstanceString(element, "ADSK_Группирование"));
        if (byGroup == Role.Uerm)
            return Role.Uerm;

        Role role = TextRules.ElectricalRoleByName(GetPrimaryName(element));
        if (role == Role.None)
            role = TextRules.ElectricalRoleByName(text);
        if (role == Role.None)
            role = byGroup;
        if (role != Role.None)
            return role;

        FamilyInstance? instance = element as FamilyInstance;
        if (instance != null && instance.SuperComponent != null)
        {
            role = GetRole(instance.SuperComponent);
            if (role != Role.None)
                return role;
        }

        if (cat == CatLightingFixture)
            return Role.Lighting;
        if (cat == CatLightingDevice || cat == CatElecFixture)
            return Role.WiringDevice;
        if (cat == CatElecEquipment)
            return Role.PowerPanel;
        return Role.None;
    }

    // ================================ СС ================================

    /// <summary>
    /// Модель СС: подсистема по шифру комплекта, затем по рабочему набору, затем по родителю.
    /// Лотки без подсистемы — в общие кабеленесущие 04.05.03.19. АГПТ/СКЗ и аксессуары шкафов не заполняются.
    /// </summary>
    private Classification ClassifyLowCurrent(Element element, Classification result)
    {
        result.Discipline = Discipline.LowCurrent;
        ElementType? type = _doc.GetElement(element.GetTypeId()) as ElementType;
        if (type != null && LowCurrentTable.Accessory.IsMatch(type.FamilyName ?? string.Empty))
        {
            result.Skip = true;
            result.Reason = "аксессуар телекоммуникационного шкафа";
            return result;
        }

        bool found;
        string? abbreviation;
        LowCurrentSystem? system = GetLowCurrentSystem(element, out found, out abbreviation);
        if (found && system == null)
        {
            result.Skip = true;
            result.Reason = "подсистема " + abbreviation + " заполняется вручную";
            return result;
        }

        result.Role = GetLowCurrentRole(element);
        if (system == null && !IsCableSupportRole(result.Role))
        {
            result.Reason = "не определена подсистема СС (нет шифра комплекта / рабочего набора в таблице)";
            return result;
        }

        result.Item = LowCurrentTable.Item(system, result.Role);
        if (result.Item == null)
            result.Reason = "нет позиции " + result.Role + " в подсистеме " + system?.Name;
        return result;
    }

    private static bool IsCableSupportRole(Role role)
    {
        return role == Role.CableTray || role == Role.CableTrayFitting || role == Role.CableSupport ||
               role == Role.FireproofBox || role == Role.CablePenetration;
    }

    private LowCurrentSystem? GetLowCurrentSystem(Element element, out bool found, out string? abbreviation)
    {
        foreach (string name in new[] { "ADSK_Штамп_Шифр комплекта", "ADSK_Штамп_Шифр_комплекта" })
        {
            LowCurrentSystem? system = LowCurrentTable.Find(GetTypeOrInstanceString(element, name), out found, out abbreviation);
            if (found)
                return system;
        }

        if (_doc.IsWorkshared)
        {
            Workset workset = _doc.GetWorksetTable().GetWorkset(element.WorksetId);
            if (workset != null)
            {
                LowCurrentSystem? system = LowCurrentTable.Find(workset.Name, out found, out abbreviation);
                if (found)
                    return system;
            }
        }

        FamilyInstance? instance = element as FamilyInstance;
        if (instance != null && instance.SuperComponent != null)
            return GetLowCurrentSystem(instance.SuperComponent, out found, out abbreviation);

        found = false;
        abbreviation = null;
        return null;
    }

    /// <summary>Лотки, каналы (в т.ч. смоделированные воздуховодами) — по категории; остальное по наименованию, иначе оконечное устройство.</summary>
    private Role GetLowCurrentRole(Element element)
    {
        ElementId cat = element.Category != null ? element.Category.Id : ElementId.InvalidElementId;
        string text = GetElementText(element);

        if (cat == CatCableTray || cat == CatConduit || cat == CatDuct || cat == CatFlexDuct)
            return TextRules.FireproofBox.IsMatch(text) ? Role.FireproofBox : Role.CableTray;
        if (cat == CatCableTrayFitting || cat == CatConduitFitting || cat == CatDuctFitting)
        {
            if (TextRules.FireproofBox.IsMatch(text))
                return Role.FireproofBox;
            return LowCurrentTable.StraightCover.IsMatch(text) ? Role.CableTray : Role.CableTrayFitting;
        }
        if (cat == CatWire)
            return Role.Cable;

        Role role = LowCurrentTable.RoleByName(GetPrimaryName(element));
        if (role == Role.None)
            role = LowCurrentTable.RoleByName(text);
        return role != Role.None ? role : Role.Terminal;
    }

    /// <summary>
    /// Задание на отверстие (Пересечение_Стена_Круглое, Заглушка прямоугольная, Отверстие в плите_Шахта):
    /// не электрическая категория, семейство/тип про отверстие, а наименование ничего электрического не называет.
    /// </summary>
    private bool IsOpeningTask(Element element)
    {
        ElementId cat = element.Category != null ? element.Category.Id : ElementId.InvalidElementId;
        if (cat == CatElecEquipment || cat == CatElecFixture || cat == CatLightingFixture || cat == CatLightingDevice ||
            cat == CatCableTray || cat == CatCableTrayFitting || cat == CatConduit || cat == CatConduitFitting || cat == CatWire)
            return false;

        ElementType? type = _doc.GetElement(element.GetTypeId()) as ElementType;
        string family = type != null ? type.FamilyName + " | " + type.Name : element.Name;
        if (!TextRules.OpeningTask.IsMatch(family ?? string.Empty))
            return false;
        return TextRules.ElectricalRoleByName(GetPrimaryName(element)) == Role.None;
    }

    private string GetTypeOrInstanceString(Element element, string name)
    {
        string? value = GetString(element.LookupParameter(name));
        if (string.IsNullOrWhiteSpace(value))
        {
            Element? type = _doc.GetElement(element.GetTypeId());
            if (type != null)
                value = GetString(type.LookupParameter(name));
        }
        return value ?? string.Empty;
    }

    private string GetPrimaryName(Element element)
    {
        return GetTypeOrInstanceString(element, "ADSK_Наименование").ToLowerInvariant();
    }

    /// <summary>Некоторые роли однозначно указывают на раздел независимо от системы.</summary>
    private Discipline ApplyRoleConstraints(Role role, Discipline discipline)
    {
        switch (role)
        {
            case Role.SanitaryFixture:
                return Discipline.Sewer;
            case Role.Trap:
                return discipline == Discipline.Storm ? Discipline.Storm : Discipline.Sewer;
            case Role.Funnel:
                return Discipline.Storm;
            case Role.FireCabinet:
                return Discipline.Fire;
            case Role.Sprinkler:
            case Role.Nozzle:
                return discipline == Discipline.Gas ? Discipline.Gas : Discipline.Fire;
            case Role.HeatingDevice:
            case Role.TempHeatingDevice:
                return Discipline.Heating;
            case Role.HeatCurtain:
                return discipline == Discipline.HeatSupply ? Discipline.HeatSupply : Discipline.Vent;
            case Role.WindowInlet:
                return Discipline.Vent;
            case Role.Grille:
            case Role.Duct:
            case Role.DuctFitting:
            case Role.DuctGroup:
                return IsAir(discipline) ? discipline : Discipline.Vent;
            case Role.PowderModule:
                return Discipline.Powder;
            case Role.Coolant:
                return Discipline.Cooling;
            case Role.Detector:
            case Role.CableSupport:
                if (discipline == Discipline.Gas || discipline == Discipline.Powder)
                    return discipline;
                if (_documentDiscipline == Discipline.Gas || _documentDiscipline == Discipline.Powder)
                    return _documentDiscipline;
                return Discipline.None;
            default:
                return discipline;
        }
    }

    private static bool IsAir(Discipline discipline)
    {
        return discipline == Discipline.Vent || discipline == Discipline.Smoke || discipline == Discipline.Cooling;
    }

    // ================================ Раздел ================================

    private Discipline GetDiscipline(Element element)
    {
        Discipline discipline;
        if (_disciplineCache.TryGetValue(element.Id, out discipline))
            return discipline;

        _disciplineCache[element.Id] = Discipline.None; // защита от циклов
        discipline = ComputeDiscipline(element);
        _disciplineCache[element.Id] = discipline;
        return discipline;
    }

    private Discipline ComputeDiscipline(Element element)
    {
        // 1. Изоляция — по изолируемому элементу.
        InsulationLiningBase? insulation = element as InsulationLiningBase;
        if (insulation != null)
        {
            Element? host = _doc.GetElement(insulation.HostElementId);
            if (host != null)
            {
                Discipline hostDiscipline = GetDiscipline(host);
                if (hostDiscipline != Discipline.None)
                    return hostDiscipline;
            }
        }

        if (_fixedSection == Discipline.Fire)
            return ComputeFireModelDiscipline(element);
        if (_fixedSection == Discipline.Vent)
            return ComputeVentModelDiscipline(element);
        if (_fixedSection == Discipline.Heating)
            return ComputeHeatingModelDiscipline(element);
        if (_fixedSection == Discipline.Water)
            return ComputeWaterModelDiscipline(element);
        if (_fixedSection != Discipline.None)
            return _fixedSection;

        List<MEPSystemType> systemTypes = new List<MEPSystemType>();
        string systemText = GetSystemText(element, systemTypes);
        NetworkDomain domain = GetDomain(element, systemTypes);

        // 2. Имя / сокращение / тип системы.
        Discipline discipline = domain == NetworkDomain.Air
            ? TextRules.MatchAirSystem(systemText)
            : TextRules.MatchPipeSystem(systemText);
        if (discipline != Discipline.None)
            return discipline;

        // 3. Классификация системы Revit.
        foreach (MEPSystemType systemType in systemTypes)
        {
            discipline = FromClassification(systemType.SystemClassification);
            if (discipline != Discipline.None && (domain == NetworkDomain.Air) == IsAir(discipline))
                return discipline;
        }

        // 4. Родительское семейство (вложенные общие семейства).
        FamilyInstance? instance = element as FamilyInstance;
        if (instance != null && instance.SuperComponent != null)
        {
            discipline = GetDiscipline(instance.SuperComponent);
            if (discipline != Discipline.None)
                return discipline;
        }

        // 5. Наименование элемента.
        discipline = TextRules.MatchElementName(GetElementText(element));
        if (discipline != Discipline.None && (domain != NetworkDomain.Air || IsAir(discipline)))
            return discipline;

        // 6. Шифр документа (ШФТ-СОБ-1-ПТ -> пожаротушение).
        if (domain == NetworkDomain.Air)
            return IsAir(_documentDiscipline) ? _documentDiscipline : Discipline.Vent;
        return _documentDiscipline;
    }

    /// <summary>
    /// Модель вентиляции: общеобменная (04.05.01.09) или противодымная (04.05.01.10) — по системе
    /// (1ДВ1ж, 1ДП2.1ж, ВД1а...), затем по родительскому семейству и наименованию ("клапан дымовой").
    /// Собственно трубопроводы (обвязка калориферов) — по системе трубопроводов, по умолчанию теплоснабжение.
    /// </summary>
    private Discipline ComputeVentModelDiscipline(Element element)
    {
        List<MEPSystemType> systemTypes = new List<MEPSystemType>();
        string systemText = GetSystemText(element, systemTypes);

        if (IsPipework(element))
        {
            Discipline pipeDiscipline = TextRules.MatchPipeSystem(systemText);
            return pipeDiscipline != Discipline.None ? pipeDiscipline : Discipline.HeatSupply;
        }

        Discipline airDiscipline = TextRules.MatchAirSystem(systemText);
        if (airDiscipline != Discipline.None)
            return airDiscipline;

        FamilyInstance? instance = element as FamilyInstance;
        if (instance != null && instance.SuperComponent != null)
            return GetDiscipline(instance.SuperComponent);

        if (TextRules.SmokeElement.IsMatch(GetElementText(element)))
            return Discipline.Smoke;

        return Discipline.Vent;
    }

    /// <summary>
    /// Модель отопления: отопление (04.05.01.07) или теплоснабжение (04.05.01.08).
    /// 1) группирующий параметр спецификации ("Теплоснабжение жилой части"...) — работает и для
    ///    неподключённых насосов/завес; 2) система: Т12/Т22 — теплоснабжение, Т1/Т2/Т11/Т21 — отопление;
    /// 3) тепловые завесы — теплоснабжение; 4) родительское семейство; иначе отопление.
    /// </summary>
    private Discipline ComputeHeatingModelDiscipline(Element element)
    {
        Discipline byHint = GetHeatingSectionHint(element);
        if (byHint != Discipline.None)
            return byHint;

        Discipline bySystem = TextRules.MatchHeatingSystem(GetSystemText(element, new List<MEPSystemType>()));
        if (bySystem != Discipline.None)
            return bySystem;

        if (GetRole(element) == Role.HeatCurtain)
            return Discipline.HeatSupply;

        FamilyInstance? instance = element as FamilyInstance;
        if (instance != null && instance.SuperComponent != null)
            return GetDiscipline(instance.SuperComponent);

        return Discipline.Heating;
    }

    /// <summary>Значение группирующего параметра "Отопление ..." / "Теплоснабжение ...".</summary>
    private Discipline GetHeatingSectionHint(Element element)
    {
        string? value = FindParameterValue(element, TextRules.HeatingSectionHint);
        if (value == null)
            return Discipline.None;
        return value.Trim().ToLowerInvariant().StartsWith("тепло") ? Discipline.HeatSupply : Discipline.Heating;
    }

    /// <summary>
    /// Модель ВК: водопровод (01), бытовая канализация (02), ливневая (03).
    /// Сантехприборы — всегда бытовая канализация, смесители — водопровод.
    /// Иначе: группирующий параметр ("3. Канализация дождевая (К2)"), система, родитель, наименование.
    /// </summary>
    private Discipline ComputeWaterModelDiscipline(Element element)
    {
        Role role = GetRole(element);
        if (role == Role.SanitaryFixture)
            return Discipline.Sewer;
        if (role == Role.Mixing)
            return Discipline.Water;

        Discipline discipline = Discipline.None;
        string? hint = FindParameterValue(element, TextRules.WaterSewerSectionHint);
        if (hint != null)
            discipline = TextRules.MatchWaterSewerSystem(TextRules.NormalizeSystemText(hint));
        if (discipline == Discipline.None)
            discipline = TextRules.MatchWaterSewerSystem(GetSystemText(element, new List<MEPSystemType>()));

        if (discipline == Discipline.None)
        {
            FamilyInstance? instance = element as FamilyInstance;
            if (instance != null && instance.SuperComponent != null)
                discipline = GetDiscipline(instance.SuperComponent);
        }

        if (discipline == Discipline.None)
        {
            // Кровельная воронка без системы — ливнёвка, трап — бытовая; капельная воронка в К4 решается системой выше.
            if (role == Role.Funnel)
                return Discipline.Storm;
            if (role == Role.Trap)
                return Discipline.Sewer;

            Discipline byName = TextRules.MatchElementName(GetElementText(element));
            if (byName == Discipline.Water || byName == Discipline.Sewer || byName == Discipline.Storm)
                return byName;
        }

        return discipline == Discipline.Water || discipline == Discipline.Sewer || discipline == Discipline.Storm
            ? discipline
            : Discipline.None;
    }

    /// <summary>Первое значение текстового параметра экземпляра или типа, подходящее под шаблон.</summary>
    private string? FindParameterValue(Element element, Regex pattern)
    {
        string? value = FindParameterValue(element.Parameters, pattern);
        if (value != null)
            return value;
        Element? type = _doc.GetElement(element.GetTypeId());
        return type != null ? FindParameterValue(type.Parameters, pattern) : null;
    }

    private static string? FindParameterValue(ParameterSet parameters, Regex pattern)
    {
        foreach (Parameter parameter in parameters)
        {
            if (parameter.StorageType != StorageType.String)
                continue;
            string? value = parameter.AsString();
            if (!string.IsNullOrEmpty(value) && pattern.IsMatch(value))
                return value;
        }
        return null;
    }

    /// <summary>Трубы, фитинги и арматура трубопроводов (кроме смесительных узлов, которые идут в оборудование вентиляции).</summary>
    private bool IsPipework(Element element)
    {
        ElementId cat = element.Category != null ? element.Category.Id : ElementId.InvalidElementId;
        if (cat == CatPipe || cat == CatFlexPipe || cat == CatPipeFitting)
            return true;
        return cat == CatPipeAccessory && GetRole(element) != Role.Mixing;
    }

    /// <summary>
    /// Модель ПТ: по умолчанию 04.05.01.04. Газовое/порошковое — только по явным признакам
    /// в системе, наименовании, марке, примечании или комментариях (АГПТ, МГП, АППТ, МПП...).
    /// </summary>
    private Discipline ComputeFireModelDiscipline(Element element)
    {
        string text = GetSystemText(element, new List<MEPSystemType>()) + " | " +
                      TextRules.NormalizeSystemText(GetElementText(element) + " | " + GetNoteText(element));
        Discipline special = TextRules.MatchSpecialExtinguishing(text);
        if (special != Discipline.None)
            return special;

        FamilyInstance? instance = element as FamilyInstance;
        if (instance != null && instance.SuperComponent != null)
            return GetDiscipline(instance.SuperComponent);

        return Discipline.Fire;
    }

    private string GetNoteText(Element element)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(GetString(element.LookupParameter("ADSK_Примечание"))).Append(" | ");
        sb.Append(GetString(element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS))).Append(" | ");

        Element? type = _doc.GetElement(element.GetTypeId());
        if (type != null)
        {
            sb.Append(GetString(type.LookupParameter("ADSK_Примечание"))).Append(" | ");
            sb.Append(GetString(type.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS)));
        }
        return sb.ToString();
    }

    private static Discipline FromClassification(MEPSystemClassification classification)
    {
        switch (classification.ToString())
        {
            case "SupplyAir":
            case "ReturnAir":
            case "ExhaustAir":
            case "OtherAir":
                return Discipline.Vent;
            case "SupplyHydronic":
            case "ReturnHydronic":
                return Discipline.Heating;
            case "DomesticHotWater":
            case "DomesticColdWater":
                return Discipline.Water;
            case "Sanitary":
            case "Vent":
                return Discipline.Sewer;
            case "Storm":
                return Discipline.Storm;
            case "FireProtectWet":
            case "FireProtectDry":
            case "FireProtectPreaction":
            case "FireProtectOther":
                return Discipline.Fire;
            default:
                return Discipline.None;
        }
    }

    private NetworkDomain GetDomain(Element element, List<MEPSystemType>? systemTypes)
    {
        ElementId cat = element.Category != null ? element.Category.Id : ElementId.InvalidElementId;
        if (cat == CatDuct || cat == CatFlexDuct || cat == CatDuctFitting || cat == CatDuctAccessory ||
            cat == CatDuctTerminal || cat == CatDuctInsulation || cat == CatDuctLining)
            return NetworkDomain.Air;
        if (cat == CatPipe || cat == CatFlexPipe || cat == CatPipeFitting || cat == CatPipeAccessory ||
            cat == CatPipeInsulation || cat == CatSprinkler || cat == CatPlumbing)
            return NetworkDomain.Pipe;

        if (systemTypes == null)
        {
            systemTypes = new List<MEPSystemType>();
            GetSystemText(element, systemTypes);
        }
        if (systemTypes.Count == 0)
            return NetworkDomain.Other;

        // Оборудование, подключённое к воздуховодам (вентустановки и т.п.), считаем вентиляционным.
        foreach (MEPSystemType systemType in systemTypes)
        {
            if (systemType is MechanicalSystemType)
                return NetworkDomain.Air;
        }
        return NetworkDomain.Pipe;
    }

    // ================================ Тексты ================================

    /// <summary>Имена, сокращения, типы и классификации всех систем элемента — одной строкой.</summary>
    private string GetSystemText(Element element, List<MEPSystemType> systemTypes)
    {
        StringBuilder sb = new StringBuilder();
        HashSet<ElementId> seenTypes = new HashSet<ElementId>();

        foreach (MEPSystem system in GetSystems(element))
        {
            sb.Append(system.Name).Append(" | ");
            AddSystemType(_doc.GetElement(system.GetTypeId()) as MEPSystemType, sb, systemTypes, seenTypes);
        }

        AddSystemType(GetTypeFromParameter(element, BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM), sb, systemTypes, seenTypes);
        AddSystemType(GetTypeFromParameter(element, BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM), sb, systemTypes, seenTypes);

        sb.Append(GetString(element.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM))).Append(" | ");
        sb.Append(GetString(element.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM))).Append(" | ");
        sb.Append(GetString(element.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM))).Append(" | ");
        sb.Append(GetString(element.LookupParameter("ADSK_Имя системы"))).Append(" | ");
        sb.Append(GetString(element.LookupParameter("ADSK_Система"))).Append(" | ");
        sb.Append(GetString(element.LookupParameter("ADSK_Группирование"))).Append(" | ");
        sb.Append(GetString(element.LookupParameter("ИмяСистемы")));

        return TextRules.NormalizeSystemText(sb.ToString());
    }

    private MEPSystemType? GetTypeFromParameter(Element element, BuiltInParameter parameter)
    {
        Parameter p = element.get_Parameter(parameter);
        if (p == null || p.StorageType != StorageType.ElementId)
            return null;
        return _doc.GetElement(p.AsElementId()) as MEPSystemType;
    }

    private static void AddSystemType(MEPSystemType? type, StringBuilder sb, List<MEPSystemType> types, HashSet<ElementId> seen)
    {
        if (type == null || !seen.Add(type.Id))
            return;
        types.Add(type);
        sb.Append(type.Name).Append(" | ").Append(type.Abbreviation).Append(" | ");
    }

    private static IEnumerable<MEPSystem> GetSystems(Element element)
    {
        List<MEPSystem> systems = new List<MEPSystem>();

        MEPCurve? curve = element as MEPCurve;
        if (curve != null)
        {
            if (curve.MEPSystem != null)
                systems.Add(curve.MEPSystem);
            return systems;
        }

        FamilyInstance? instance = element as FamilyInstance;
        if (instance == null || instance.MEPModel == null || instance.MEPModel.ConnectorManager == null)
            return systems;

        foreach (Connector connector in instance.MEPModel.ConnectorManager.Connectors)
        {
            if (connector.Domain != Domain.DomainPiping && connector.Domain != Domain.DomainHvac)
                continue;
            try
            {
                MEPSystem? system = connector.MEPSystem;
                if (system != null && !systems.Exists(s => s.Id == system.Id))
                    systems.Add(system);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // У некоторых коннекторов система недоступна — пропускаем.
            }
        }
        return systems;
    }

    /// <summary>Семейство, типоразмер, ADSK_Наименование и ADSK_Марка — строка для поиска ключевых слов.</summary>
    private string GetElementText(Element element)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(element.Name).Append(" | ");
        sb.Append(GetString(element.LookupParameter("ADSK_Наименование"))).Append(" | ");
        sb.Append(GetString(element.LookupParameter("ADSK_Марка"))).Append(" | ");

        ElementType? type = _doc.GetElement(element.GetTypeId()) as ElementType;
        if (type != null)
        {
            sb.Append(type.FamilyName).Append(" | ");
            sb.Append(type.Name).Append(" | ");
            sb.Append(GetString(type.LookupParameter("ADSK_Наименование"))).Append(" | ");
            sb.Append(GetString(type.LookupParameter("ADSK_Марка")));
        }
        return sb.ToString().ToLowerInvariant();
    }

    private static string GetString(Parameter? parameter)
    {
        if (parameter == null || !parameter.HasValue)
            return string.Empty;
        string? value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        return value ?? string.Empty;
    }
}
