using ClassifierCode.Engine.Facts;
using LegacyRole = ClassifierCodeLegacy.Models.Role;
using LegacyDiscipline = ClassifierCodeLegacy.Models.Discipline;
using LegacyRules = ClassifierCodeLegacy.Dictionaries.TextRules;
using Shouldly;
using static ToadTools.Tests.ClassifierCode.Donskoy;

namespace ToadTools.Tests.ClassifierCode;

/// <summary>
///     Режимы ИОС встроенного профиля против старой логики: сравнение с замороженной копией TextRules на большом
///     наборе строк и пограничные случаи, ожидания которых выведены чтением старого ElementClassifier.
/// </summary>
public class DonskoyIosParityTests
{
    // ---------------- Набор строк для сравнения ----------------

    /// <summary>Слова из простых шаблонов профиля и примеры к регуляркам.</summary>
    private static readonly Lazy<List<string>> Words = new(() =>
    {
        var words = new HashSet<string>();
        foreach (var pattern in Profile.Source.PatternSets.Values.SelectMany(v => v))
        {
            if (pattern.StartsWith("re:")) continue;
            words.Add(pattern.Replace("=", "").Replace("*", ""));
        }

        string[] extra =
        [
            "теплосчётчик", "реле давления", "реле потока", "гильза для монтажа термопреобразователя", "гильза", "маты", "мат",
            "k-flex", "energoflex", "ei60", "ei 90", "rwl", "каменной ваты", "минеральной ваты", "цилиндр из минеральной ваты",
            "шкаф пожарный", "пожарный шкаф", "кран пожарный", "пожарный кран", "рс-50", "гм-50", "2 х пк", "пк", "кплм", "дппк",
            "надвижная гильза", "пресс-фитинг", "rapid", "хомут rapid", "хомут sml", "кронштейн для умывальника",
            "кронштейн для крепления раковины", "временное отопление", "тепловая пушка", "регистр отопления", "отопительный прибор",
            "этажный узел", "узел распределительный", "концевая секция", "распределительный шкаф", "муфта противопожарная",
            "противопожарная муфта", "внутренний блок", "наружный блок", "vrf", "ккб", "шу1", "шкаф управления", "щит управления",
            "прибор управления", "ппу", "шак", "узел учета", "прибор учета", "кип", "фильтр", "компенсатор", "refnet", "гофра",
            "опора", "опоры", "опор", "подвес", "гф-021", "пф-115", "грунт", "грунтовка", "вентиль", "вентиля", "вентилятор"
        ];
        foreach (var word in extra) words.Add(word);
        return words.OrderBy(w => w, StringComparer.Ordinal).ToList();
    });

    /// <summary>Тексты: каждое слово отдельно, с соседями и в паре с каждым 7-м словом (детерминированно).</summary>
    private static IEnumerable<string> Texts()
    {
        var words = Words.Value;
        for (var i = 0; i < words.Count; i++)
        {
            yield return words[i];
            yield return "изделие " + words[i] + " dn20";
            for (var j = i % 7; j < words.Count; j += 7)
            {
                yield return words[i] + " " + words[j];
                yield return words[j] + " для " + words[i];
            }
        }
    }

    private static string? LegacyByName(string primary, string full, bool air, bool isMep)
    {
        var role = LegacyRules.RoleByName(primary, air, isMep);
        if (role == LegacyRole.None) role = LegacyRules.RoleByName(full, air, isMep);
        return role == LegacyRole.None ? null : Camel(role.ToString());
    }

    /// <summary>Тип элемента в старом ComputeRole для категорий, где роль — только по наименованию.</summary>
    private static List<string> Differences(string category, NetworkDomain domain, string mode, Func<string, string, string?> legacy)
    {
        var engine = Engine(mode);
        var differences = new List<string>();
        foreach (var text in Texts())
        {
            // Основное имя — первая половина, полное — вся строка: проверяется и переход с одного на другое.
            var split = text.IndexOf(' ');
            var primary = split > 0 && text.Length % 2 == 0 ? text.Substring(0, split) : text.Length % 3 == 0 ? string.Empty : text;
            var facts = F(category, primary, text, domain: domain);
            var expected = legacy(primary.ToLowerInvariant(), (" | " + primary + " |  | " + text + " |  |  | ").ToLowerInvariant());
            var actual = engine.Classify(facts).Type;
            if (expected != actual) differences.Add($"«{primary}» / «{text}»: было {expected ?? "—"}, стало {actual ?? "—"}");
            if (differences.Count > 20) break;
        }

        return differences;
    }

    [Test]
    [MatrixDataSource]
    public void Diff_GenericCategory_ByName([Matrix("water", "vent", "cooling")] string mode, [Matrix(NetworkDomain.Pipe, NetworkDomain.Air, NetworkDomain.Other)] NetworkDomain domain)
    {
        var air = domain == NetworkDomain.Air || (domain == NetworkDomain.Other && mode == "vent");
        Differences("OST_GenericModel", domain, mode, (p, f) => LegacyByName(p, f, air, false)).ShouldBeEmpty();
    }

    [Test]
    [MatrixDataSource]
    public void Diff_Accessory_ByNameElseArmature([Matrix("OST_PipeAccessory", "OST_DuctAccessory")] string category)
    {
        var domain = category == "OST_PipeAccessory" ? NetworkDomain.Pipe : NetworkDomain.Air;
        Differences(category, domain, "heating", (p, f) => LegacyByName(p, f, domain == NetworkDomain.Air, true) ?? "armature").ShouldBeEmpty();
    }

    [Test]
    [MatrixDataSource]
    public void Diff_MechanicalEquipment([Matrix(NetworkDomain.Pipe, NetworkDomain.Air, NetworkDomain.Other)] NetworkDomain domain)
    {
        var air = domain == NetworkDomain.Air;
        Differences("OST_MechanicalEquipment", domain, "cooling", (p, f) =>
        {
            // Старый ComputeRole, ветка оборудования.
            var role = LegacyRules.RoleByName(p, air, false);
            if (role == LegacyRole.None) role = LegacyRules.RoleByName(f, air, false);
            switch (role)
            {
                case LegacyRole.None:
                case LegacyRole.Pipe:
                case LegacyRole.Duct:
                case LegacyRole.PipeGroup:
                case LegacyRole.DuctGroup:
                case LegacyRole.Insulation:
                case LegacyRole.FireThermalProtection:
                case LegacyRole.Coolant:
                    return "equipment";
                case LegacyRole.DuctFitting:
                    return air && LegacyRules.Refnet.IsMatch(f) ? "pipeFitting" : "ductFitting";
                default:
                    return Camel(role.ToString());
            }
        }).ShouldBeEmpty();
    }

    // ---------------- Системы ----------------

    private static IEnumerable<string> SystemTexts()
    {
        string[] codes =
        [
            "В0", "В1", "В2", "В11", "В21", "Т1", "Т2", "Т3", "Т4", "Т11", "Т12", "Т21", "Т22", "Т31", "Т41", "К1", "К2", "К3", "К4", "К21",
            "КН", "КНД", "ХС", "Х1", "ТС", "ДВ", "ДП", "ВД", "ДУ", "ПД", "П1", "В", "АУПТ", "ПТ", "ВПВ", "АГПТ", "ГПТ", "АППТ", "ППТ",
            "VRF", "VRV", "СПЛИТ", "МГП", "МПП", "ХВС", "ГВС", "КАНАЛИЗАЦИЯ", "ВОДОСТОК", "ЛИВНЕВКА", "ДОЖДЕВАЯ", "ФЕКАЛЬНАЯ",
            "ВОДОПРОВОД", "ВОДОСНАБЖЕНИЕ", "ХОЛОДНОЙ ВОДЫ", "ГОРЯЧЕЙ ВОДЫ", "ПИТЬЕВОЙ", "ХОЛОДОСНАБЖЕНИЕ", "ФРЕОН", "ГЛИКОЛЬ",
            "ХЛАДОН", "КОНДИЦИОНИРОВАНИЕ", "ТЕПЛОСНАБЖЕНИЕ", "КАЛОРИФЕР", "ОТОПЛЕНИЕ", "ДЫМОУДАЛЕНИЕ", "ПОДПОР", "КОМПЕНСАЦИЯ",
            "ПОЖАРНЫЙ", "СПРИНКЛЕРНАЯ", "ДРЕНЧЕРНАЯ", "ГАЗОВОЕ ПОЖАРОТУШЕНИЕ", "ПОРОШКОВОЕ", "ВНУТРЕННИЙ ВОДОСТОК", "ВОДООТВЕДЕНИЕ",
            "ПВ2", "AGPT", "B1", "T12", "K2", "ГРТ", "АРРТ", "МОДУЛЬ ГАЗОВОГО", "МОДУЛЬ ПОРОШКОВОГО", "ПОРОШКОВОЕ ПОЖАРОТУШЕНИЕ"
        ];
        string[] prefixes = ["", "1", "П", "А"];
        string[] suffixes = ["", "1", ".1", "Ж", "11", " "];
        foreach (var code in codes)
        foreach (var prefix in prefixes)
        foreach (var suffix in suffixes)
            yield return prefix + code + suffix;
        for (var i = 0; i < codes.Length; i++)
            yield return codes[i] + ", " + codes[(i * 5 + 3) % codes.Length];
    }

    private static List<string> DisciplineDifferences(string mode, string category, Func<string, string?> legacy)
    {
        var engine = Engine(mode);
        var differences = new List<string>();
        foreach (var system in SystemTexts())
        {
            var expected = legacy(LegacyRules.NormalizeSystemText(system + " |  |  |  |  | "));
            var actual = engine.Classify(F(category, system: system)).Discipline;
            if (expected != actual) differences.Add($"«{system}»: было {expected ?? "—"}, стало {actual ?? "—"}");
            if (differences.Count > 20) break;
        }

        return differences;
    }

    private static string? D(LegacyDiscipline discipline) => discipline == LegacyDiscipline.None ? null : Camel(discipline.ToString());

    [Test]
    public void Diff_Vent_Pipework() =>
        DisciplineDifferences("vent", "OST_PipeCurves", s =>
        {
            var d = LegacyRules.MatchPipeSystem(s);
            return D(d == LegacyDiscipline.None ? LegacyDiscipline.HeatSupply : d);
        }).ShouldBeEmpty();

    [Test]
    public void Diff_Vent_Ducts() =>
        DisciplineDifferences("vent", "OST_DuctCurves", s =>
        {
            var d = LegacyRules.MatchAirSystem(s);
            return D(d == LegacyDiscipline.None ? LegacyDiscipline.Vent : d);
        }).ShouldBeEmpty();

    [Test]
    public void Diff_Heating() =>
        DisciplineDifferences("heating", "OST_PipeCurves", s =>
        {
            var d = LegacyRules.MatchHeatingSystem(s);
            return D(d == LegacyDiscipline.None ? LegacyDiscipline.Heating : d);
        }).ShouldBeEmpty();

    [Test]
    public void Diff_Water() =>
        DisciplineDifferences("water", "OST_PipeCurves", s => D(LegacyRules.MatchWaterSewerSystem(s))).ShouldBeEmpty();

    [Test]
    public void Diff_Fire()
    {
        // Старый ComputeFireModelDiscipline: система | нормализованные (имя | примечание); имена пустые.
        var engine = Engine("fire");
        foreach (var system in SystemTexts())
        {
            var text = LegacyRules.NormalizeSystemText(system + " |  |  |  |  | ") + " | " +
                       LegacyRules.NormalizeSystemText(" |  |  |  |  |  |  |  |  |  | ");
            var d = LegacyRules.MatchSpecialExtinguishing(text);
            var expected = D(d == LegacyDiscipline.None ? LegacyDiscipline.Fire : d);
            engine.Classify(F("OST_PipeCurves", system: system)).Discipline.ShouldBe(expected, system);
        }
    }

    [Test]
    public void Diff_DocumentTitle()
    {
        string[] titles =
        [
            "ШФТ-СОБ-1-ПТ", "DON_PT_SOB", "DON_OVK_SOB", "Проект-ОВВ", "ОВО_корпус1", "КНД", "VK-1", "ВК2", "ЭОМ", "СС_АУПС",
            "ПТ-ВК", "ВК-ПТ", "АУПТ", "АГПТ", "ТС", "ДУ", "ПДВ", "СКВ", "ЕОМ", "Проект1", "СКУД", "ОТОП", "VENT", "KND"
        ];
        foreach (var title in titles)
        {
            var legacy = ClassifierCodeLegacy.Models.SectionOption.All.FirstOrDefault(s => s.Section == LegacySection(title))?.Title;
            var mode = ModeDetector(title);
            mode.ShouldBe(legacy, title);
        }
    }

    private static string? ModeDetector(string title)
    {
        var key = global::ClassifierCode.Engine.Running.ModeDetector.DetectMode(Profile, title);
        return key == null ? null : Profile.Source.Modes.First(m => m.Key == key).Title;
    }

    /// <summary>Старый ClassifierCodeService.DetectSection.</summary>
    private static LegacyDiscipline LegacySection(string title) => LegacyRules.MatchDocumentTitle(title) switch
    {
        LegacyDiscipline.Fire or LegacyDiscipline.Gas or LegacyDiscipline.Powder => LegacyDiscipline.Fire,
        LegacyDiscipline.Cooling => LegacyDiscipline.Cooling,
        LegacyDiscipline.Vent or LegacyDiscipline.Smoke => LegacyDiscipline.Vent,
        LegacyDiscipline.Heating or LegacyDiscipline.HeatSupply => LegacyDiscipline.Heating,
        LegacyDiscipline.Water or LegacyDiscipline.Sewer or LegacyDiscipline.Storm => LegacyDiscipline.Water,
        LegacyDiscipline.Electrical => LegacyDiscipline.Electrical,
        LegacyDiscipline.LowCurrent => LegacyDiscipline.LowCurrent,
        _ => LegacyDiscipline.None
    };

    // ---------------- Пограничные случаи (ожидания — из старого ElementClassifier) ----------------

    public static IEnumerable<Func<(string Mode, FakeFacts Facts, string Expected)>> Cases()
    {
        // Роль по наименованию: побеждает слово, стоящее раньше.
        yield return () => ("heating", F("OST_PipeAccessory", "Клапан обратный для вентилятора", system: "Т1"), "04.05.01.07.05");
        yield return () => ("vent", F("OST_MechanicalEquipment", "Осевая вентиляторная установка с клапаном", system: "В1"), "04.05.01.09.01");
        // Маты … EI60 — огнезащита, хотя «маты» раньше.
        yield return () => ("vent", F("OST_DuctInsulations", "Маты из каменной ваты EI60"), "04.05.01.09.07");
        yield return () => ("vent", F("OST_GenericModel", "Маты из каменной ваты EI60"), "04.05.01.09.07");
        // Сантехприбор раньше крепежа; соединительная деталь раньше крепежа.
        yield return () => ("water", F("OST_GenericModel", "Кронштейн для умывальника"), "04.05.01.02.05");
        yield return () => ("water", F("OST_PipeAccessory", "Хомут Rapid SML", system: "К1"), "04.05.01.02.02.02");
        yield return () => ("heating", F("OST_PipeAccessory", "Гильза для монтажа термопреобразователя", system: "Т1"), "04.05.01.07.06");
        // Модель ПТ: В1 на вводе остаётся ПТ; явные АГПТ/АППТ; Газ раньше Порошка в одной строке.
        yield return () => ("fire", F("OST_PipeCurves", system: "В1"), "04.05.01.04.02.01");
        yield return () => ("fire", new FakeFacts
        {
            Category = "OST_PipeCurves", Domain = NetworkDomain.Pipe, SystemText = "АППТ", Instance = { ["ADSK_Примечание"] = "АГПТ" }
        }, "04.05.01.05.02.01");
        // В порошковом нет позиции для труб и замены для них тоже нет.
        yield return () => ("fire", F("OST_PipeCurves", system: "АППТ"), "none");
        yield return () => ("fire", F("OST_PipeInsulations", "Изоляция"), "04.05.01.04.02");
        // Вентиляция: противодымная по системе, неподключённый кронштейн — воздуховоды, обвязка без системы — теплоснабжение.
        yield return () => ("vent", F("OST_DuctCurves", system: "1ДВ1ж"), "04.05.01.10.02.01");
        yield return () => ("vent", F("OST_GenericModel", "Кронштейн"), "04.05.01.09.02");
        yield return () => ("heating", F("OST_GenericModel", "Кронштейн"), "04.05.01.07.04");
        yield return () => ("vent", F("OST_PipeCurves"), "04.05.01.08.03.01");
        yield return () => ("vent", F("OST_PipeAccessory", "Клапан"), "04.05.01.08.04");
        yield return () => ("vent", F("OST_DuctAccessory", "Клапан противопожарный дымовой"), "04.05.01.10.04");
        yield return () => ("vent", F("OST_PipeAccessory", "КИП манометр", system: "В1"), "04.05.01.01.04");
        yield return () => ("vent", F("OST_DuctAccessory", "Манометр"), "04.05.01.09.03");
        // Отопление: группирующий параметр, завесы.
        yield return () => ("heating", new FakeFacts
        {
            Category = "OST_MechanicalEquipment", Instance = { ["ADSK_Группирование"] = "Теплоснабжение УК", ["ADSK_Наименование"] = "Насос" }
        }, "04.05.01.08.02");
        yield return () => ("heating", F("OST_MechanicalEquipment", "Тепловая завеса"), "04.05.01.08.01");
        yield return () => ("heating", F("OST_MechanicalEquipment", "Радиатор"), "04.05.01.07.01");
        // ВК: унитаз с двумя системами, смеситель с пустым наименованием, воронка без системы, радиатор с «канализ».
        yield return () => ("water", F("OST_PlumbingFixtures", "Унитаз", system: "К1, В1.1"), "04.05.01.02.05");
        yield return () => ("water", F("OST_PlumbingFixtures", family: "Смеситель для мойки"), "04.05.01.01.06");
        yield return () => ("water", F("OST_PlumbingFixtures", "Мойка", family: "Смеситель для мойки"), "04.05.01.02.05");
        yield return () => ("water", F("OST_PlumbingFixtures", "Воронка кровельная"), "04.05.01.03.03");
        yield return () => ("water", F("OST_PlumbingFixtures", "Трап"), "04.05.01.02.04");
        yield return () => ("water", F("OST_PipeAccessory", "Трап", system: "К2"), "04.05.01.03.03");
        yield return () => ("water", F("OST_GenericModel", "Радиатор канализ"), "none");
        yield return () => ("water", new FakeFacts
        {
            Category = "OST_PipeCurves", Domain = NetworkDomain.Pipe, Instance = { ["ADSK_Группирование"] = "3. Канализация дождевая (К2)" }
        }, "04.05.01.03.01.01");
        yield return () => ("water", F("OST_PipeCurves"), "none");
        // Изоляция берёт раздел трубы.
        yield return () =>
        {
            var insulation = F("OST_PipeInsulations", "Изоляция K-Flex");
            insulation.Host = F("OST_PipeCurves", system: "Т12");
            return ("heating", insulation, "04.05.01.08.07");
        };
        // Рефнет в оборудовании на воздуховодах — фитинг труб; кондиционирование.
        yield return () => ("cooling", F("OST_MechanicalEquipment", "Разветвитель REFNET", domain: NetworkDomain.Air), "04.05.01.11.02.02");
        yield return () => ("cooling", F("OST_DuctCurves"), "04.05.01.11.08.01");
        // Вложенный элемент пожарного шкафа.
        yield return () =>
        {
            var nested = F("OST_PipeAccessory", "Клапан");
            nested.Parent = F("OST_MechanicalEquipment", "Шкаф пожарный ШПК");
            return ("fire", nested, "04.05.01.04.06");
        };
        // Замены: КИП в вентиляции → арматура; изоляция в ПТ → трубы.
        yield return () => ("fire", F("OST_GenericModel", "Изоляция"), "04.05.01.04.02");
        // «Не учитывать» в марке типа — 999.
        yield return () => ("water", new FakeFacts { Category = "OST_PipeCurves", Type = { ["ADSK_Марка"] = "НЕ_УЧИТЫВАТЬ" } }, "999");
        // Не распознано.
        yield return () => ("heating", F("OST_GenericModel", "Нечто"), "none");
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public void Case_MatchesOldCode((string Mode, FakeFacts Facts, string Expected) c)
    {
        var result = Engine(c.Mode).Classify(c.Facts);
        Code(result).ShouldBe(c.Expected, string.Join("\n", result.Trace.Select(t => t.Stage + ": " + t.Text)));
    }
}
