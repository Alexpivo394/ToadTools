using System.Text.RegularExpressions;
using ClassifierCode.Models;

namespace ClassifierCode.Dictionaries;

/// <summary>
/// СС проекта Донской: подсистема — по шифру комплекта (ШФТ-СОБ-АУПС) или рабочему набору,
/// позиция — кабельная продукция / оконечные устройства / шкафы и оборудование, лотки — 04.05.03.19.
/// </summary>
public static class LowCurrentTable
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Общие кабеленесущие изделия СС.</summary>
    public static readonly LowCurrentSystem CableSupport = new LowCurrentSystem("Кабеленесущие изделия СС");

    private static readonly Dictionary<string, LowCurrentSystem> Systems = new Dictionary<string, LowCurrentSystem>();

    // Аббревиатуры шифров и рабочих наборов. null — подсистема заполняется вручную (АГПТ, СКЗ).
    private static readonly Dictionary<string, string?> Abbreviations = new Dictionary<string, string?>
    {
        { "ЛВС", "01" }, { "МСПД", "01" },
        { "СКС", "02" },
        { "ТФ", "03" },
        { "РТ", "04" }, { "РФ", "04" }, { "ГОЧС", "04" },
        { "СКПТ", "05" },
        { "СОТС", "06" },
        { "СКУД", "07" },
        { "СОТ", "08" }, { "СВН", "08" },
        { "ДМФ", "09" }, { "СДС", "09" },
        { "АПС", "10" }, { "АУПС", "10" }, { "СПЗ", "10" },
        { "АППЗ", "11" },
        { "СОУЭ", "12" },
        { "АК", "13" },
        { "ДИС", "14" },
        { "АСКУЭР", "15" },
        { "МГН", "16" },
        { "ОДС", "17" },
        { "ОЗДС", "18" },
        { "МК", "19" }, { "МК1", "19" }, { "МК2", "19" }, { "ЛОТКИ", "19" },
        { "СУС", "20" }, { "СУСС", "20" },
        { "АГПТ", null }, { "СКЗ", null }
    };

    static LowCurrentTable()
    {
        Standard("01", "ЛВС");
        System("02", "СКС", "01", "02", "04");
        Standard("03", "телефонизации");
        System("04", "РТ, ГО и ЧС", null, "02", "03");
        System("05", "СКПТ", "02", "03", "04");
        Standard("06", "СОТС");
        Standard("07", "СКУД");
        Standard("08", "СОТ");
        Standard("09", "ДМФ");
        LowCurrentSystem aps = System("10", "АПС", "01", "03", "04");
        Add(aps, Role.CableSupport, "04.05.03.10.02", "Кабеленесущие изделия");
        Add(aps, Role.CableTray, "04.05.03.10.02.01", "Прямые участки");
        Add(aps, Role.CableTrayFitting, "04.05.03.10.02.02", "Фасонные элементы");
        Standard("11", "АППЗ");
        Standard("12", "СОУЭ");
        Standard("13", "АК");
        Standard("14", "ДИС");
        Standard("15", "АСКУЭР");
        Standard("16", "СС МГН");
        Standard("17", "ОДС");
        Standard("18", "ОЗДС");
        Standard("20", "СУС");
        Standard("21", "SmartHouse");
        Standard("22", "Multimedia");
        Standard("23", "ВКСС");

        Add(CableSupport, Role.CableSupport, "04.05.03.19", "Кабеленесущие изделия СС");
        Add(CableSupport, Role.CableTray, "04.05.03.19.01", "Прямые участки");
        Add(CableSupport, Role.CableTrayFitting, "04.05.03.19.02", "Фасонные элементы");
        Add(CableSupport, Role.FireproofBox, "04.05.03.19.03", "Короба огнезащитные");
        Add(CableSupport, Role.CablePenetration, "04.05.03.19.04", "Устройство кабельных проходок СС");
        Systems["19"] = CableSupport;
    }

    private static void Standard(string number, string name)
    {
        System(number, name, "01", "02", "03");
    }

    /// <summary>Подсистема с номерами позиций кабельной продукции, оконечных устройств и шкафов/оборудования.</summary>
    private static LowCurrentSystem System(string number, string name, string? cable, string terminal, string equipment)
    {
        LowCurrentSystem system = new LowCurrentSystem(name);
        string code = "04.05.03." + number;
        if (cable != null)
            Add(system, Role.Cable, code + "." + cable, "Кабельная продукция");
        Add(system, Role.Terminal, code + "." + terminal, "Оконечные устройства");
        Add(system, Role.Equipment, code + "." + equipment, EquipmentName(number));
        Systems[number] = system;
        return system;
    }

    // Название позиции шкафов/оборудования в классификаторе отличается по подсистемам.
    private static string EquipmentName(string number)
    {
        switch (number)
        {
            case "02": return "Шкафы управления, стойки";
            case "06":
            case "18": return "Оборудование";
            case "10":
            case "11":
            case "16": return "Шкафы управления, оборудование";
            default: return "Шкафы управления, стойки, оборудование";
        }
    }

    private static void Add(LowCurrentSystem system, Role role, string code, string description)
    {
        system.Items[role] = new ClassifierItem(code, description);
    }

    /// <summary>
    /// Подсистема по тексту (шифр комплекта "ШФТ-СОБ-АУПС", рабочий набор "Лотки СС Маликов").
    /// Ищем с конца. found=false — аббревиатуры нет; found=true и null — подсистема заполняется вручную.
    /// </summary>
    public static LowCurrentSystem? Find(string? text, out bool found, out string? abbreviation)
    {
        found = false;
        abbreviation = null;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string[] tokens = Regex.Split(TextRules.NormalizeSystemText(text), @"[-_.,\s()]+");
        for (int i = tokens.Length - 1; i >= 0; i--)
        {
            string? number;
            if (!Abbreviations.TryGetValue(tokens[i], out number))
                continue;
            found = true;
            abbreviation = tokens[i];
            return number != null ? Systems[number] : null;
        }
        return null;
    }

    // ---------- Роль по наименованию (побеждает слово, стоящее в тексте раньше) ----------

    /// <summary>Аксессуары телекоммуникационных шкафов — по классификатору никуда не относятся.</summary>
    public static readonly Regex Accessory = new Regex(@"аксессуар", Opts);

    /// <summary>Крышка прямого участка лотка — к прямым участкам.</summary>
    public static readonly Regex StraightCover = new Regex(@"крышк\w*\s+на\s+прям|\brp_крышк", Opts);

    private sealed class NameRule
    {
        public readonly Role Role;
        public readonly Regex Pattern;

        public NameRule(Role role, string pattern)
        {
            Role = role;
            Pattern = new Regex(pattern, Opts);
        }
    }

    private static readonly List<NameRule> NameRules = new List<NameRule>
    {
        new NameRule(Role.FireproofBox, @"огнезащ\w*\s+короб|короб\w*\s+огнезащ|огнестойк\w*\s+короб|короб\w*\s+огнестойк"),
        new NameRule(Role.CablePenetration, @"проходк"),
        new NameRule(Role.CableTray,
            @"крышк\w*\s+на\s+прям|\bлот(ок|ки)\b|магистральн\w*\s+кабельн\w*\s+канал|кабель-канал|металлорукав|\bгофр"),
        new NameRule(Role.CableTrayFitting,
            @"крышк|ответвител\w*\s+(dpt|dpx|т-образ|крестообраз)|т-ответвител|\bугол\s+(cd|cp|cs|ср|сd|сs)|изменяем\w*\s+угол|" +
            @"поворот|переходник|соединител|пластин\w*\s+крепежн|т-образн"),
        // Явные оконечные — чтобы "Извещатель ручной ... со встроенным изолятором" не ушёл в оборудование.
        new NameRule(Role.Terminal,
            @"извещател|оповещател|громкоговорит|\bкамер|видеокамер|считывател|\bкнопк|\bзамок|защелк|\bдатчик|" +
            @"антенн|видеопанел|домофон|вызывн\w*\s+панел|радиорозетк|устройств\w*\s+дистанц|\bудп\b|\bипр\b|" +
            @"сигнализатор|\bбарьер|доводчик|шлагбаум|\bрадар|точк\w*\s+доступа"),
        new NameRule(Role.Cable,
            @"^\s*(кабел|провод)(?!\w*\s+(лот|канал|рост|короб|проходк|продукц))|\bкпс|\bкспв|\bкуфэ|\butp\b|\bftp\b|витая\s+пара|\bшввп\b"),
        new NameRule(Role.Equipment,
            @"прибор\w*\s+при[её]мно|\bппк|\bппу\b|ивэпр|источник\w*\s+(вторичн|бесперебойн|питани)|" +
            @"блок\w*\s+(бесперебойн|питани|преобразоват|высоковольт|индикац|сопряжен)|аккумулят|\bакб\b|бокс\w*\s+резервн|" +
            @"\bшкаф|\bстойк|\bщит|контроллер|коммутатор|маршрутизатор|сервер|видеорегистратор|регистратор|\bибп\b|\bups\b|" +
            @"\bмодул|\bметк|изолятор|бустер|репитер|делител|разветвител|сплиттер|ответвител\w*\s+абонент|усилител|" +
            @"трансформатор|грозозащит|\bпульт|\brack\b|медиаконвертер|патч-панел")
    };

    /// <summary>Роль элемента СС по наименованию. None — значит оконечное устройство (или не распознано).</summary>
    public static Role RoleByName(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Role.None;

        Role best = Role.None;
        int bestIndex = int.MaxValue;
        foreach (NameRule rule in NameRules)
        {
            Match match = rule.Pattern.Match(text);
            if (match.Success && match.Index < bestIndex)
            {
                best = rule.Role;
                bestIndex = match.Index;
            }
        }
        return best;
    }

    /// <summary>Позиция подсистемы; кабеленесущие, короба и проходки без своей позиции — в 04.05.03.19.</summary>
    public static ClassifierItem? Item(LowCurrentSystem? system, Role role)
    {
        ClassifierItem? item;
        if (system != null && system.Items.TryGetValue(role, out item))
            return item;
        if (CableSupport.Items.TryGetValue(role, out item))
            return item;
        return null;
    }
}
