using ClassifierCode.Engine.Facts;
using LegacyRole = ClassifierCodeLegacy.Models.Role;
using LegacyRules = ClassifierCodeLegacy.Dictionaries.TextRules;
using LegacyLowCurrent = ClassifierCodeLegacy.Dictionaries.LowCurrentTable;
using LegacyTable = ClassifierCodeLegacy.Dictionaries.ClassifierTable;
using Shouldly;
using static ToadTools.Tests.ClassifierCode.Donskoy;

namespace ToadTools.Tests.ClassifierCode;

/// <summary>ЭОМ, СС и исключения встроенного профиля против старой логики.</summary>
public class DonskoyEomSsParityTests
{
    private static readonly Lazy<List<string>> Words = new(() =>
    {
        var words = new HashSet<string>(Profile.Source.PatternSets.Values.SelectMany(v => v)
            .Where(p => !p.StartsWith("re:")).Select(p => p.Replace("=", "").Replace("*", "")));
        string[] extra =
        [
            "кабель ввг", "провод пв1", "кабель лоток", "кабельный канал", "кабель-канал", "ящик учетный распределительный", "шкаф учета",
            "огнезащитный короб", "короб огнезащитный", "угол внутренний", "угол наружный", "соединитель лотка", "т-образный",
            "датчик движения", "датчик присутствия", "трансформатор ремонтный", "трансформатор понижающий", "светоуказатель",
            "уравнивание потенциалов", "крышка на прямой", "rp_крышка", "ответвитель dpt", "т-ответвитель", "угол cd", "изменяемый угол",
            "пластина крепежная", "магистральный кабельный канал", "лотки", "лоток", "витая пара", "utp", "ftp", "кпс", "кспв",
            "прибор приемно", "источник вторичного", "блок питания", "бокс резервный", "ответвитель абонентский", "точка доступа",
            "вызывная панель", "устройство дистанционного", "пересечение", "отверстие", "проём", "шахта", "заглушка", "аксессуар",
            "уэрм", "светильник", "щит", "щитки, шкафы, ящики, пульты", "светильники, светоуказатели", "электроустановочные изделия",
            "лотки", "кабели и провода", "не учитывать"
        ];
        foreach (var word in extra) words.Add(word);
        return words.OrderBy(w => w, StringComparer.Ordinal).ToList();
    });

    private static IEnumerable<(string Primary, string Text, int Index)> Texts()
    {
        var words = Words.Value;
        var n = 0;
        for (var i = 0; i < words.Count; i++)
        {
            yield return (words[i], words[i], n++);
            yield return ("", "изделие " + words[i], n++);
            for (var j = i % 5; j < words.Count; j += 5)
            {
                yield return (words[i], words[i] + " " + words[j], n++);
                yield return ("", words[j] + " " + words[i], n++);
            }
        }
    }

    private static string Full(string primary, string text) => (" | " + primary + " |  | " + text + " |  |  | ").ToLowerInvariant();

    [Test]
    public void Diff_Electrical()
    {
        string[] groups = ["", "УЭРМ", "Светильники, светоуказатели", "Щитки, шкафы, ящики, пульты", "Кабели и провода", "Лотки"];
        var engine = Engine("electrical");
        var differences = new List<string>();
        foreach (var (primary, text, index) in Texts())
        {
            var group = groups[index % groups.Length];
            var facts = new FakeFacts
            {
                Category = "OST_ElectricalFixtures", FamilyName = text,
                Instance = { ["ADSK_Наименование"] = primary, ["ADSK_Группирование"] = group }
            };

            // Старый ComputeElectricalRole для электроприборов.
            var byGroup = LegacyRules.ElectricalRoleByGroup(group);
            var role = byGroup == LegacyRole.Uerm ? LegacyRole.Uerm : LegacyRules.ElectricalRoleByName(primary.ToLowerInvariant());
            if (role == LegacyRole.None) role = LegacyRules.ElectricalRoleByName(Full(primary, text));
            if (role == LegacyRole.None) role = byGroup;
            if (role == LegacyRole.None) role = LegacyRole.WiringDevice;
            var item = LegacyTable.Find(ClassifierCodeLegacy.Models.Discipline.Electrical, role);
            var expected = LegacyRules.Excluded.IsMatch(primary) ? "999" : item?.Code ?? "none";

            var actual = Code(engine.Classify(facts));
            if (expected != actual) differences.Add($"«{primary}» / «{text}» / «{group}»: было {expected}, стало {actual}");
            if (differences.Count > 20) break;
        }

        differences.ShouldBeEmpty();
    }

    [Test]
    public void Diff_LowCurrent()
    {
        string[] kits =
        [
            "", "ШФТ-СОБ-АУПС", "ШФТ-СОБ-СКЗ", "АГПТ", "ЛВС", "МК1", "ШФТ_СОБ_СОУЭ", "СКУД (корпус 1)", "АУПС-СКЗ", "СКЗ-АУПС",
            "ТФ", "РТ", "PT", "ДМФ", "СУСС", "ЛОТКИ", "АПС.1", "ОДС,ОЗДС", "АСКУЭР", "МГН", "ХХХ"
        ];
        var engine = Engine("lowcurrent");
        var differences = new List<string>();
        foreach (var (primary, text, index) in Texts())
        {
            var kit = kits[index % kits.Length];
            var facts = new FakeFacts
            {
                Category = "OST_FireAlarmDevices", FamilyName = text, TypeName = "",
                Instance = { ["ADSK_Наименование"] = primary, ["ADSK_Штамп_Шифр комплекта"] = kit }
            };

            var actual = Code(engine.Classify(facts));
            var expected = LegacyLowCurrentCode(primary, text, kit);
            if (expected != actual) differences.Add($"«{primary}» / «{text}» / «{kit}»: было {expected}, стало {actual}");
            if (differences.Count > 20) break;
        }

        differences.ShouldBeEmpty();
    }

    /// <summary>Старый Classify в режиме СС для элемента не кабельной категории без родителя и рабочего набора.</summary>
    private static string LegacyLowCurrentCode(string primary, string text, string kit)
    {
        if (LegacyRules.Excluded.IsMatch(primary)) return "999";
        // IsOpeningTask: семейство | тип про отверстие, а основное имя ничего электрического не называет.
        if (LegacyRules.OpeningTask.IsMatch(text + " | ") &&
            LegacyRules.ElectricalRoleByName(primary.ToLowerInvariant()) == LegacyRole.None) return "skip";
        if (LegacyLowCurrent.Accessory.IsMatch(text)) return "skip";

        var system = LegacyLowCurrent.Find(kit, out var found, out _);
        if (found && system == null) return "skip";

        var role = LegacyLowCurrent.RoleByName(primary.ToLowerInvariant());
        if (role == LegacyRole.None) role = LegacyLowCurrent.RoleByName(Full(primary, text));
        if (role == LegacyRole.None) role = LegacyRole.Terminal;

        var cable = role is LegacyRole.CableTray or LegacyRole.CableTrayFitting or LegacyRole.CableSupport or LegacyRole.FireproofBox
            or LegacyRole.CablePenetration;
        if (system == null && !cable) return "none";
        return LegacyLowCurrent.Item(system, role)?.Code ?? "none";
    }

    // ---------------- Пограничные случаи ----------------

    private static FakeFacts Ss(string category, string name, string kit = "", string? workset = null, string family = "") => new()
    {
        Category = category, FamilyName = family, Workset = workset,
        Instance = { ["ADSK_Наименование"] = name, ["ADSK_Штамп_Шифр комплекта"] = kit }
    };

    public static IEnumerable<Func<(string Mode, FakeFacts Facts, string Expected)>> Cases()
    {
        // ЭОМ
        yield return () => ("electrical", F("OST_ElectricalEquipment", "Ящик учетный распределительный"), "04.05.02.03");
        yield return () => ("electrical", F("OST_ElectricalEquipment", "Шкаф учета"), "04.05.02.02");
        yield return () => ("electrical", F("OST_ElectricalEquipment", "ВРУ-1"), "04.05.02.01");
        yield return () => ("electrical", F("OST_CableTray", "Короб огнезащитный"), "04.05.02.07");
        yield return () => ("electrical", F("OST_CableTray", "Лоток перфорированный"), "04.05.02.04.01");
        yield return () => ("electrical", F("OST_CableTrayFitting", "Поворот"), "04.05.02.04.02");
        yield return () => ("electrical", F("OST_GenericModel", family: "Пересечение_Стена_Круглое"), "skip");
        yield return () => ("electrical", F("OST_GenericModel", "Светильник", family: "Пересечение_Стена_Круглое"), "04.05.02.09");
        yield return () => ("electrical", F("OST_LightingFixtures", family: "Пересечение_Стена_Круглое"), "04.05.02.09");
        yield return () => ("electrical", new FakeFacts
        {
            Category = "OST_ElectricalEquipment", Instance = { ["ADSK_Наименование"] = "Щит ЩР-1", ["ADSK_Группирование"] = "УЭРМ" }
        }, "04.05.02.03");
        yield return () => ("electrical", F("OST_LightingDevices", "Нечто"), "04.05.02.10");
        yield return () => ("electrical", F("OST_GenericModel", "Нечто"), "none");
        yield return () => ("electrical", F("OST_LightingFixtures", "Светильник НЕ УЧИТЫВАТЬ"), "999");
        // СС
        yield return () => ("lowcurrent", Ss("OST_FireAlarmDevices", "Извещатель пожарный дымовой", "ШФТ-СОБ-АУПС"), "04.05.03.10.03");
        yield return () => ("lowcurrent", Ss("OST_FireAlarmDevices", "Извещатель", "ШФТ-СОБ-АУПС", "СКЗ"), "04.05.03.10.03");
        yield return () => ("lowcurrent", Ss("OST_FireAlarmDevices", "Извещатель", "", "СКЗ"), "skip");
        yield return () => ("lowcurrent", Ss("OST_CableTray", "Лоток", "", "Лотки СС Маликов"), "04.05.03.19.01");
        yield return () => ("lowcurrent", Ss("OST_CableTray", "Лоток", "ШФТ-СОБ-АПС"), "04.05.03.10.02.01");
        yield return () => ("lowcurrent", Ss("OST_CableTray", "Лоток", "ЛВС"), "04.05.03.19.01");
        yield return () => ("lowcurrent", Ss("OST_CableTray", "Лоток"), "04.05.03.19.01");
        yield return () => ("lowcurrent", Ss("OST_CommunicationDevices", "Кабель КПСнг", "РТ"), "none");
        yield return () => ("lowcurrent", Ss("OST_CommunicationDevices", "Кабель КПСнг", "ШФТ-СОБ-СКС"), "04.05.03.02.01");
        yield return () => ("lowcurrent", Ss("OST_CommunicationDevices", "Полка", "СКС", family: "Аксессуар шкафа"), "skip");
        yield return () => ("lowcurrent", Ss("OST_CommunicationDevices", "Розетка НЕ_УЧИТЫВАТЬ", "СКС"), "999");
        yield return () => ("lowcurrent", Ss("OST_SecurityDevices", "Камера"), "none");
        yield return () =>
        {
            var nested = Ss("OST_FireAlarmDevices", "Оповещатель");
            nested.Parent = Ss("OST_FireAlarmDevices", "Блок", "ШФТ-СОБ-СОУЭ");
            return ("lowcurrent", nested, "04.05.03.12.02");
        };
        yield return () => ("lowcurrent", Ss("OST_CableTrayFitting", "Крышка на прямой участок", "СКС"), "04.05.03.19.01");
        yield return () => ("lowcurrent", Ss("OST_CableTrayFitting", "Крышка поворота", "СКС"), "04.05.03.19.02");
        yield return () => ("lowcurrent", Ss("OST_DuctCurves", "Короб огнестойкий", "СОУЭ"), "04.05.03.19.03");
        yield return () => ("lowcurrent", Ss("OST_GenericModel", "", family: "Отверстие в плите_Шахта"), "skip");
        yield return () => ("lowcurrent", new FakeFacts
        {
            Category = "OST_CommunicationDevices", Type = { ["ADSK_Штамп_Шифр_комплекта"] = "ШФТ-СОБ-СОТ" }, Instance = { ["ADSK_Наименование"] = "Видеорегистратор" }
        }, "04.05.03.08.03");
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public void Case_MatchesOldCode((string Mode, FakeFacts Facts, string Expected) c)
    {
        var result = Engine(c.Mode).Classify(c.Facts);
        Code(result).ShouldBe(c.Expected, string.Join("\n", result.Trace.Select(t => t.Stage + ": " + t.Text)));
    }
}
