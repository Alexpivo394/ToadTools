using System.Text;
using System.Text.RegularExpressions;
using ClassifierCode.Models;

namespace ClassifierCode.Dictionaries;

/// <summary>
/// Текстовые правила: распознавание раздела по имени/сокращению системы,
/// по наименованию элемента и по шифру документа, а также роли по наименованию.
/// </summary>
public static class TextRules
{
    // Начало "слова" для шифров систем: перед совпадением не должно быть буквы или цифры (В2 но не ПВ2).
    private const string L = @"(?<![А-ЯЁA-Z0-9])";
    // То же, но цифра перед шифром допустима: номер корпуса в начале имени системы (1ДВ1ж, 1ДП2.1ж, 1Кнд...).
    private const string Lb = @"(?<![А-ЯЁA-Z])";
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private sealed class Rule<T>
    {
        public readonly T Value;
        public readonly Regex Pattern;

        public Rule(T value, string pattern)
        {
            Value = value;
            Pattern = new Regex(pattern, Opts);
        }
    }

    // ---------- Системы трубопроводов (текст системы нормализован в верхний регистр кириллицей) ----------
    private static readonly List<Rule<Discipline>> PipeSystemRules = new List<Rule<Discipline>>
    {
        new Rule<Discipline>(Discipline.Cooling, Lb + @"КНД|КОНДИЦ|VR[FV]|СПЛИТ"),
        new Rule<Discipline>(Discipline.Gas, L + @"А?ГПТ|ГАЗОВ\w*\s+ПОЖАР"),
        new Rule<Discipline>(Discipline.Powder, L + @"А?ППТ|ПОРОШК"),
        new Rule<Discipline>(Discipline.Fire, L + @"В2|" + L + @"ВПВ|" + L + @"А?У?В?ПТ(?![А-ЯЁ])|ПОЖАР|СПРИНКЛ|ДРЕНЧ"),
        new Rule<Discipline>(Discipline.Storm, L + @"К2(?!\d)|ВОДОСТОК|ЛИВН|ДОЖДЕВ"),
        new Rule<Discipline>(Discipline.Sewer, L + @"К\d|" + L + @"КН(?![А-ЯЁ])|КАНАЛИЗ|ФЕКАЛ"),
        new Rule<Discipline>(Discipline.Water, L + @"В[01]|" + L + @"Т[34](?!\d)|ХВС|ГВС|ВОДОПРОВОД|ВОДОСНАБ|ХОЛОДН\w*\s+ВОД|ГОРЯЧ\w*\s+ВОД|ПИТЬЕВ"),
        new Rule<Discipline>(Discipline.Cooling, L + @"Х\d|" + L + @"ХС(?![А-ЯЁ])|ХОЛОДОСНАБ|ФРЕОН|ГЛИКОЛ|ХЛАД|КОНДИЦ"),
        new Rule<Discipline>(Discipline.HeatSupply, HeatSupplyPattern),
        new Rule<Discipline>(Discipline.Heating, HeatingPattern)
    };

    // Шифры проекта Донской: отопление — Т11.x/Т21.x (и просто Т1/Т2), теплоснабжение — Т12.x/Т22.x.
    private const string HeatSupplyPattern = Lb + @"Т[12]2(?!\d)|" + Lb + @"ТС(?![А-ЯЁ])|ТЕПЛОСНАБ|КАЛОРИФ";
    private const string HeatingPattern = Lb + @"Т[12]1?(?!\d)|ОТОП";

    // Водоснабжение и водоотведение: К2/водосток/дождевая — ливнёвка, остальные К — бытовая (в т.ч. К4
    // условно чистые и аварийные стоки), В0/В1/Т3/Т4 — водопровод. Канализация раньше водопровода:
    // у унитаза системы "К1, В1.1".
    private static readonly List<Rule<Discipline>> WaterSewerSystemRules = new List<Rule<Discipline>>
    {
        new Rule<Discipline>(Discipline.Storm, Lb + @"К2(?!\d)|ВОДОСТОК|ЛИВН|ДОЖДЕВ|ВНУТРЕНН\w*\s+ВОДОСТ"),
        new Rule<Discipline>(Discipline.Sewer, Lb + @"К\d|" + Lb + @"КН(?![А-ЯЁ])|КАНАЛИЗ|ФЕКАЛ|ВОДООТВЕД"),
        new Rule<Discipline>(Discipline.Water, Lb + @"В[01]|" + Lb + @"Т[34](?!\d)|ХВС|ГВС|ВОДОПРОВОД|ВОДОСНАБ|ГОРЯЧ\w*\s+ВОД|ХОЛОДН\w*\s+ВОД|ПИТЬЕВ")
    };

    /// <summary>Заголовок группы спецификации: "1. Водопровод хозяйственно-питьевой ... (В1.1)", "3. Канализация дождевая (К2)".</summary>
    public static readonly Regex WaterSewerSectionHint = new Regex(
        @"^\s*\d+\.?\s*(водопровод|трубопровод|канализац|водосток|ливнев|дождев|водоотвед|водоснаб)", Opts);

    private static readonly List<Rule<Discipline>> HeatingSystemRules = new List<Rule<Discipline>>
    {
        new Rule<Discipline>(Discipline.HeatSupply, HeatSupplyPattern),
        new Rule<Discipline>(Discipline.Heating, HeatingPattern)
    };

    /// <summary>Значение группирующего параметра спецификации: "Отопление жилой части", "Теплоснабжение УК"...</summary>
    public static readonly Regex HeatingSectionHint = new Regex(@"^\s*(теплоснабжени|отоплени)", Opts);

    // ---------- Системы воздуховодов ----------
    private static readonly List<Rule<Discipline>> AirSystemRules = new List<Rule<Discipline>>
    {
        new Rule<Discipline>(Discipline.Cooling, Lb + @"КНД|КОНДИЦ"),
        // ДВ — дымоудаление, ДП — подпор/компенсация, ВД/ДУ/ПД — прочие обозначения противодымных систем.
        new Rule<Discipline>(Discipline.Smoke, Lb + @"ДВ|" + Lb + @"ДП|" + Lb + @"ВД|" + Lb + @"ДУ|" + Lb + @"ПД|ДЫМ|ПОДПОР|КОМПЕНС"),
        new Rule<Discipline>(Discipline.Cooling, @"ХОЛОД")
    };

    // ---------- Газовое / порошковое пожаротушение внутри модели ПТ (текст нормализован) ----------
    private static readonly List<Rule<Discipline>> SpecialExtinguishingRules = new List<Rule<Discipline>>
    {
        new Rule<Discipline>(Discipline.Gas,
            L + @"А?[GГ][РП]Т(?![А-ЯЁ])|ГАЗОВ\w*\s+ПОЖАР|МОДУЛ\w*\s+ГАЗ|" + L + @"МГП(?![А-ЯЁ])"),
        new Rule<Discipline>(Discipline.Powder,
            L + @"А?(ПП|РР)Т(?![А-ЯЁ])|МОДУЛ\w*\s+ПОРОШК|" + L + @"МПП(?![А-ЯЁ])|ПОРОШКОВ\w*\s+ПОЖАРОТУШ")
    };

    /// <summary>Признаки противодымного элемента в наименовании (клапан дымовой и т.п.).</summary>
    private const string SmokeElementPattern = @"противодым|дымоудал|подпор|\bдымов";

    public static readonly Regex SmokeElement = new Regex(SmokeElementPattern, Opts);

    // ---------- Наименование элемента -> раздел (если по системе определить не удалось) ----------
    private static readonly List<Rule<Discipline>> ElementDisciplineRules = new List<Rule<Discipline>>
    {
        new Rule<Discipline>(Discipline.Powder, @"модул\w*\s+порошк|\bмпп\b|\bаппт\b"),
        new Rule<Discipline>(Discipline.Gas, @"модул\w*\s+газ|\bмгп\b|газов\w*\s+пожар"),
        new Rule<Discipline>(Discipline.Fire, @"огнетуш|\bпожарн|спринкл|ороситель|дренч|\bшпк\b"),
        new Rule<Discipline>(Discipline.Heating, @"радиатор|конвектор|отопит"),
        new Rule<Discipline>(Discipline.Storm, @"воронк|водосток|ливнев"),
        new Rule<Discipline>(Discipline.Sewer, @"унитаз|раковин|умывальн|мойк|\bванн|писсуар|душев|\bтрап|канализ"),
        new Rule<Discipline>(Discipline.Smoke, SmokeElementPattern),
        new Rule<Discipline>(Discipline.Vent, @"решетк|решётк|диффузор|анемостат|вентилят|воздуховод|приточн|вытяжн"),
        new Rule<Discipline>(Discipline.Cooling, @"фанкойл|чиллер|сплит|кондиционер|\bvrf\b|\bккб\b|холодил"),
        new Rule<Discipline>(Discipline.Water, @"водомер|водоразбор|поливоч|смесител")
    };

    // ---------- Наименование элемента -> роль ----------
    public static readonly Regex FireCabinet = new Regex(
        @"шкаф\w*\s+пожарн|\bпожарн\w*\s+шкаф|\bшпк\b|ниш\w*\s+пожарн|\bпожарн\w*\s+кран|кран\w*\s+пожарн|" +
        @"головк\w*\s+пожарн|рукав\w*\s+пожарн|\bпожарн\w*\s+рукав|ствол\w*\s+пожарн|\bпожарн\w*\s+ствол|" +
        @"\bрс-?\d|огнетуш|клапан\w*\s+пожарн|\bпожарн\w*\s+клапан|\bкплм?\b|\bдппк\b|\bгм-\d|\d\s*х\s*пк\b|\bпк\b", Opts);

    public static readonly Regex Sprinkler = new Regex(@"оросит|спринклер|дренчер|распылит|насадок", Opts);
    public static readonly Regex Detector = new Regex(@"извещат", Opts);
    public static readonly Regex PowderModule = new Regex(@"модул\w*\s+порошк|\bмпп\b", Opts);

    public static readonly Regex PumpAutomation = new Regex(
        @"\bшу\w{0,3}\b|шкаф\w*\s+управлен|щит\w*\s+управлен|шкаф\w*\s+автоматик|прибор\w*\s+управлен|\bппу\b|\bшак\b|автоматика|контроллер", Opts);

    public static readonly Regex HeatCurtain = new Regex(@"завес", Opts);
    public static readonly Regex WindowInlet = new Regex(@"оконн|инфильтрац|aereco|аэреко|\bкив\b", Opts);
    public static readonly Regex Grille = new Regex(@"решетк|решётк|диффузор|анемостат|воздухораспредел|дефлектор", Opts);
    public static readonly Regex TempHeatingDevice = new Regex(@"временн\w*\s+отоп|тепл\w*\s+пушк", Opts);
    public static readonly Regex HeatingDevice = new Regex(@"радиатор|конвектор|регистр\w*\s+отоп|отопительн\w*\s+прибор", Opts);
    public static readonly Regex FireThermal = new Regex(@"огнезащ|\bei\s?\d|огнестойк", Opts);
    public static readonly Regex Insulation = new Regex(
        @"изоляц|теплоизол|k-flex|к-флекс|energoflex|энергофлекс|armaflex|rockwool|роквул|каменн\w*\s+ват|минеральн\w*\s+ват|базальт|\bмат[ыа]?\b|\brwl\b|кашированн|цилиндр\w*\s+(из\s+)?минерал", Opts);
    public static readonly Regex Trap = new Regex(@"\bтрап", Opts);
    public static readonly Regex Funnel = new Regex(@"воронк", Opts);
    public static readonly Regex Collector = new Regex(
        @"коллектор|гребенк|распределительн\w*\s+(узел|гребен|шкаф)|этажн\w*\s+узел|узел\s+(распределит|этажн)|концев\w*\s+секци", Opts);
    public static readonly Regex Mixing = new Regex(@"смесит|\bлейк", Opts);

    /// <summary>Сантехприборы (в т.ч. смоделированные обобщённой моделью) и их принадлежности.</summary>
    public static readonly Regex Sanitary = new Regex(
        @"унитаз|умывальник|раковин|\bванн|поддон|писсуар|мойк|биде|сифон|кронштейн\w*\s+для\s+(креплени\w*\s+)?(умывальн|раковин|унитаз)", Opts);

    /// <summary>Соединительные детали, которые иначе попали бы в крепёж/гильзы: надвижные гильзы, хомуты Rapid SML.</summary>
    public static readonly Regex Coupling = new Regex(
        @"надвижн\w*\s+гильз|пресс-фитинг|\brapid\b|хомут\w*\s+(rapid|sml)", Opts);

    public static readonly Regex Kip = new Regex(
        @"манометр|термометр|датчик|сигнализатор|счетчик|счётчик|водомер|расходомер|теплосч[её]т|реле\s+давлен|" +
        @"реле\s+поток|преобразовател\w*\s+давлен|термопреобразоват|индикатор|узел\s+учет|прибор\w*\s+учет|\bкип\b|" +
        // "Гильза для монтажа термопреобразователя" — принадлежность КИП, а не гильза трубопровода.
        @"гильз\w*\s+для\s+(монтаж\w*\s+)?термо", Opts);

    /// <summary>Вспомогательные материалы: крепёж, гильзы, окраска — относятся к группе "Трубы"/"Воздуховоды".</summary>
    public static readonly Regex Auxiliary = new Regex(
        @"гильз|хомут|\bопор[аыуе]?\b|подвес|крепеж|крепёж|креплени|кронштейн|шпильк|анкер|грунтовк|\bгрунт|краск|лакокрас|эмаль|\bгф-|\bпф-", Opts);

    public static readonly Regex Coolant = new Regex(@"хладоносит|гликол|хладагент", Opts);

    public static readonly Regex Armature = new Regex(
        @"клапан|\bкран|затвор|задвижк|\bвентил[ья]\b|балансир|регулятор|обратн|воздухоотвод|" +
        @"грязевик|диафрагм|редуктор|шибер|дроссел|заслонк|привод|регулирующ|" +
        // Противопожарные муфты на канализационных стояках — в спецификации стоят в арматуре.
        @"муфт\w*\s+противопожарн|противопожарн\w*\s+муфт|огнеза", Opts);

    /// <summary>Фильтр: на трубопроводе — арматура (сетчатый фильтр), в воздуховодах — оборудование.</summary>
    public static readonly Regex Filter = new Regex(@"фильтр", Opts);

    /// <summary>Компенсатор: на трубопроводе — арматура, в воздуховодах — фасонный элемент.</summary>
    public static readonly Regex Compensator = new Regex(@"компенсатор", Opts);

    public static readonly Regex Fitting = new Regex(
        @"фланец|фланц|\bотвод|переход|тройник|заглушк|муфт|ниппел|сгон|бочон|резьб|контргайк|штуцер|крестовин|" +
        @"отступ|ревизи|прочистк|патрубок|угольник|адаптер|колено|врезк|пробк|разветвител|рефнет|refnet|вставк|\bсетк|крышк|подводк|\bгофра\b|фитинг", Opts);

    /// <summary>Разветвители фреоновых трасс (рефнеты) — фитинги, даже если семейство в категории оборудования.</summary>
    public static readonly Regex Refnet = new Regex(@"разветвител|рефнет|refnet", Opts);

    /// <summary>Воздуховоды, смоделированные не системным семейством (гибкие вставки и т.п.).</summary>
    public static readonly Regex Duct = new Regex(@"воздуховод", Opts);

    /// <summary>Оборудование в категориях без своей роли (обобщённые модели, спецоборудование).</summary>
    public static readonly Regex Equipment = new Regex(
        @"насос|станци|установк|\bбак\b|гидроаккумул|модул|резервуар|теплообменник|" +
        @"(внутренн|наружн)\w*\s+блок|кондиционер|сплит|\bvr[fv]\b|фанкойл|чиллер|\bккб\b|компрессорно|" +
        @"вентилятор|калорифер|нагревател|охладител|рекуператор|глушител|шумоглуш|стакан|секци|кассет", Opts);

    // ---------- ЭОМ: наименование -> роль (побеждает слово, стоящее в тексте раньше) ----------
    private static readonly List<Rule<Role>> ElectricalNameRules = new List<Rule<Role>>
    {
        // УЭРМ раньше щитов: "Ящик учетный распределительный" — УЭРМ, а "Шкаф учета" — щит.
        new Rule<Role>(Role.Uerm, @"уэрм|\bяур\b|ящик\w*\s+уч[её]тн\w*\s+распредел|\bкэт\b|\bксс\b"),
        new Rule<Role>(Role.Lighting,
            @"светильник|светов\w*\s+указател|светоуказ|\bуказател|\bтабло|табличк|заградительн|прожектор|\bламп|" +
            @"трансформатор\w*\s+(ремонтн|понижа)|\bятп\b"),
        new Rule<Role>(Role.WiringDevice,
            @"розетк|выключател|переключател|\bкоробк|\bкнопк|\bзвонок|датчик\w*\s+(движени|присутстви)|клеммник|\bвилк"),
        new Rule<Role>(Role.Switchgear, @"электрощитов|\bвру\b|\bгрщ\b|\bавр\b|\bкру\b|\bктп\b|\bпэсп"),
        new Rule<Role>(Role.PowerPanel, @"\bщит|\bшкаф|\bящик|\bщр|\bщу|\bщм|\bщао\b|\bщо\b|\bшу\b|\bбокс"),
        new Rule<Role>(Role.Busway, @"шинопровод"),
        new Rule<Role>(Role.Grounding,
            @"молни|заземл|токоотвод|уравнивани\w*\s+потенц|\bгзш\b|\bшдуп\b|\bшуп\b|\bсуп\b|\bкуп\b"),
        new Rule<Role>(Role.FireproofBox, FireproofBoxPattern),
        new Rule<Role>(Role.CablePenetration, @"проходк"),
        new Rule<Role>(Role.Cable,
            @"^\s*(кабел|провод)(?!\w*\s+(лот|канал|рост|короб|проходк|продукц))|\bа?ввг|\bппгнг|\bпугв|\bкгтп|\bnym\b|" +
            @"\bа?пв[1-5]?\b|frls|frhf"),
        new Rule<Role>(Role.CableTrayFitting,
            @"поворот|ответвлен|т-образн|крестовин|\bпереход|заглушк|соединител\w*\s+(лотк|пластин)|угол\w*\s+(внутр|наруж)"),
        new Rule<Role>(Role.CableTray, @"лот(ок|к)|кабель-канал|кабельн\w*\s+канал|металлорукав|\bгофр|\bтруб"),
        // Крепёж лотков — в группу "Кабеленесущие изделия".
        new Rule<Role>(Role.CableSupport,
            @"консол|подвес|кронштейн|шпильк|профил|\bскоб|хомут|\bанкер|крепеж|крепёж|креплени|\bстойк")
    };

    private const string FireproofBoxPattern = @"огнезащ\w*\s+короб|короб\w*\s+огнезащ|огнестойк\w*\s+короб|короб\w*\s+огнестойк";

    /// <summary>Огнезащитный короб (может быть смоделирован лотком).</summary>
    public static readonly Regex FireproofBox = new Regex(FireproofBoxPattern, Opts);

    /// <summary>Задания на отверстия, пересечения, заглушки проёмов — в ЭОМ не кодируются.</summary>
    public static readonly Regex OpeningTask = new Regex(@"пересечени|отверсти|\bпро[её]м|\bшахт|заглушк", Opts);

    // Группирование спецификации ЭОМ. "УЭРМ" решает сразу, остальные — только если по наименованию не определилось.
    private static readonly List<Rule<Role>> ElectricalGroupRules = new List<Rule<Role>>
    {
        new Rule<Role>(Role.Uerm, @"^\s*уэрм"),
        new Rule<Role>(Role.Lighting, @"светильник|светоуказ|освещ"),
        new Rule<Role>(Role.WiringDevice, @"электроустановоч|электромонтаж"),
        new Rule<Role>(Role.CableTray, @"лот(ок|к)"),
        new Rule<Role>(Role.Cable, @"кабел|провод"),
        new Rule<Role>(Role.PowerPanel, @"щит|шкаф|ящик")
    };

    /// <summary>Роль по ADSK_Группирование ("Светильники, светоуказатели...", "Щитки, шкафы, ящики, пульты").</summary>
    public static Role ElectricalRoleByGroup(string group)
    {
        return Match(ElectricalGroupRules, group);
    }

    /// <summary>Роль элемента ЭОМ по наименованию: ключевое слово, стоящее раньше всех, побеждает.</summary>
    public static Role ElectricalRoleByName(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Role.None;

        Role best = Role.None;
        int bestIndex = int.MaxValue;
        foreach (Rule<Role> rule in ElectricalNameRules)
        {
            System.Text.RegularExpressions.Match match = rule.Pattern.Match(text);
            if (match.Success && match.Index < bestIndex)
            {
                best = rule.Value;
                bestIndex = match.Index;
            }
        }
        return best;
    }

    // ---------- Шифр документа ----------
    private static readonly Dictionary<string, Discipline> DocumentMarks = new Dictionary<string, Discipline>
    {
        { "ПТ", Discipline.Fire },
        { "АПТ", Discipline.Fire },
        { "АУПТ", Discipline.Fire },
        { "АУВПТ", Discipline.Fire },
        { "ВПВ", Discipline.Fire },
        { "ПЖ", Discipline.Fire },
        { "АГПТ", Discipline.Gas },
        { "ГПТ", Discipline.Gas },
        { "АППТ", Discipline.Powder },
        { "ТС", Discipline.HeatSupply },
        { "ТМ", Discipline.HeatSupply },
        { "ХС", Discipline.Cooling },
        { "ДУ", Discipline.Smoke },
        { "ПДВ", Discipline.Smoke },
        // Латинские шифры после нормализации двойников (DON_PT_SOB... -> "РТ")
        { "РТ", Discipline.Fire },
        { "АРТ", Discipline.Fire },
        { "АUРТ", Discipline.Fire },
        { "VРV", Discipline.Fire },
        { "АGРТ", Discipline.Gas },
        { "GРТ", Discipline.Gas },
        { "АРРТ", Discipline.Powder },
        { "ВК", Discipline.Water },
        { "ВК1", Discipline.Water },
        { "ВК2", Discipline.Water },
        { "НВК", Discipline.Water },
        { "VК", Discipline.Water },
        { "ОВО", Discipline.Heating },
        { "ОТОП", Discipline.Heating },
        { "ОVО", Discipline.Heating },
        { "ОВВ", Discipline.Vent },
        { "ВЕНТ", Discipline.Vent },
        { "ОVV", Discipline.Vent },
        { "VЕNТ", Discipline.Vent },
        { "КНД", Discipline.Cooling },
        { "КОНД", Discipline.Cooling },
        { "СКВ", Discipline.Cooling },
        { "КND", Discipline.Cooling },
        { "КОND", Discipline.Cooling },
        { "ЭОМ", Discipline.Electrical },
        { "ЭМ", Discipline.Electrical },
        { "ЭО", Discipline.Electrical },
        { "ЕОМ", Discipline.Electrical },
        { "ЕМ", Discipline.Electrical },
        { "СС", Discipline.LowCurrent },
        { "АУПС", Discipline.LowCurrent },
        { "АППЗ", Discipline.LowCurrent },
        { "СОУЭ", Discipline.LowCurrent },
        { "СКУД", Discipline.LowCurrent }
    };

    /// <summary>Приводит латинские буквы-двойники к кириллице и в верхний регистр (В1 набранное латиницей и т.п.).</summary>
    public static string NormalizeSystemText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        const string latin = "ABCEHKMOPTXY";
        const string cyrillic = "АВСЕНКМОРТХУ";
        StringBuilder sb = new StringBuilder(text!.ToUpperInvariant());
        for (int i = 0; i < sb.Length; i++)
        {
            int index = latin.IndexOf(sb[i]);
            if (index >= 0)
                sb[i] = cyrillic[index];
        }
        return sb.ToString();
    }

    public static Discipline MatchPipeSystem(string normalizedText)
    {
        return Match(PipeSystemRules, normalizedText);
    }

    public static Discipline MatchAirSystem(string normalizedText)
    {
        return Match(AirSystemRules, normalizedText);
    }

    public static Discipline MatchWaterSewerSystem(string normalizedText)
    {
        return Match(WaterSewerSystemRules, normalizedText);
    }

    public static Discipline MatchHeatingSystem(string normalizedText)
    {
        return Match(HeatingSystemRules, normalizedText);
    }

    public static Discipline MatchSpecialExtinguishing(string normalizedText)
    {
        return Match(SpecialExtinguishingRules, normalizedText);
    }

    public static Discipline MatchElementName(string text)
    {
        return Match(ElementDisciplineRules, text);
    }

    public static Discipline MatchDocumentTitle(string? title)
    {
        if (string.IsNullOrEmpty(title))
            return Discipline.None;

        string[] tokens = Regex.Split(NormalizeSystemText(title), @"[-_.\s()]+");
        // Ищем с конца: в "ШФТ-СОБ-1-ПТ" шифр раздела обычно последний.
        for (int i = tokens.Length - 1; i >= 0; i--)
        {
            Discipline discipline;
            if (DocumentMarks.TryGetValue(tokens[i], out discipline))
                return discipline;
        }
        return Discipline.None;
    }

    private enum NameRole
    {
        Fixed,      // роль из правила как есть
        Auxiliary,  // Трубы / Воздуховоды
        Fitting,    // Фитинги / Фасонные элементы
        Filter,     // арматура на трубах, оборудование в воздуховодах
        Compensator // арматура на трубах, фасонный элемент в воздуховодах
    }

    private sealed class NameRule
    {
        public readonly Role Role;
        public readonly NameRole Kind;
        public readonly Regex Pattern;
        public readonly bool NonMepOnly;

        public NameRule(Role role, NameRole kind, Regex pattern, bool nonMepOnly)
        {
            Role = role;
            Kind = kind;
            Pattern = pattern;
            NonMepOnly = nonMepOnly;
        }
    }

    // Порядок важен только при совпадении на одной и той же позиции в тексте.
    private static readonly List<NameRule> NameRules = new List<NameRule>
    {
        new NameRule(Role.Sprinkler, NameRole.Fixed, Sprinkler, false),
        new NameRule(Role.Detector, NameRole.Fixed, Detector, false),
        new NameRule(Role.PowderModule, NameRole.Fixed, PowderModule, false),
        new NameRule(Role.PumpAutomation, NameRole.Fixed, PumpAutomation, false),
        new NameRule(Role.HeatCurtain, NameRole.Fixed, HeatCurtain, false),
        new NameRule(Role.WindowInlet, NameRole.Fixed, WindowInlet, false),
        new NameRule(Role.Grille, NameRole.Fixed, Grille, false),
        new NameRule(Role.TempHeatingDevice, NameRole.Fixed, TempHeatingDevice, false),
        new NameRule(Role.HeatingDevice, NameRole.Fixed, HeatingDevice, false),
        new NameRule(Role.FireThermalProtection, NameRole.Fixed, FireThermal, false),
        new NameRule(Role.Insulation, NameRole.Fixed, Insulation, false),
        new NameRule(Role.Funnel, NameRole.Fixed, Funnel, false),
        new NameRule(Role.Trap, NameRole.Fixed, Trap, false),
        new NameRule(Role.Collector, NameRole.Fixed, Collector, false),
        new NameRule(Role.Mixing, NameRole.Fixed, Mixing, false),
        new NameRule(Role.Kip, NameRole.Fixed, Kip, false),
        // Сантехприборы и соединительные детали — раньше крепежа: "Кронштейн для умывальника", "Хомут Rapid SML".
        new NameRule(Role.SanitaryFixture, NameRole.Fixed, Sanitary, false),
        new NameRule(Role.PipeFitting, NameRole.Fitting, Coupling, false),
        new NameRule(Role.PipeGroup, NameRole.Auxiliary, Auxiliary, false),
        new NameRule(Role.Coolant, NameRole.Fixed, Coolant, true),
        new NameRule(Role.Armature, NameRole.Fixed, Armature, false),
        new NameRule(Role.Armature, NameRole.Filter, Filter, false),
        new NameRule(Role.Armature, NameRole.Compensator, Compensator, false),
        new NameRule(Role.PipeFitting, NameRole.Fitting, Fitting, false),
        new NameRule(Role.Duct, NameRole.Fixed, Duct, false),
        new NameRule(Role.Equipment, NameRole.Fixed, Equipment, false)
    };

    /// <summary>
    /// Роль по наименованию: побеждает ключевое слово, стоящее в тексте раньше всех
    /// ("Клапан обратный для вентилятора" — арматура, "Осевая вентиляторная установка ... с клапаном" — оборудование).
    /// Комплект пожарного крана и разветвители проверяются первыми.
    /// </summary>
    public static Role RoleByName(string text, bool air, bool isMepCategory)
    {
        if (string.IsNullOrEmpty(text))
            return Role.None;
        if (FireCabinet.IsMatch(text)) return Role.FireCabinet;
        if (Refnet.IsMatch(text)) return air ? Role.DuctFitting : Role.PipeFitting;

        NameRule? best = null;
        int bestIndex = int.MaxValue;
        foreach (NameRule rule in NameRules)
        {
            if (rule.NonMepOnly && isMepCategory)
                continue;
            System.Text.RegularExpressions.Match match = rule.Pattern.Match(text);
            if (match.Success && match.Index < bestIndex)
            {
                best = rule;
                bestIndex = match.Index;
            }
        }
        if (best == null)
            return Role.None;

        switch (best.Kind)
        {
            case NameRole.Auxiliary: return air ? Role.DuctGroup : Role.PipeGroup;
            case NameRole.Fitting: return air ? Role.DuctFitting : Role.PipeFitting;
            case NameRole.Filter: return air ? Role.Equipment : Role.Armature;
            case NameRole.Compensator: return air ? Role.DuctFitting : Role.Armature;
            case NameRole.Fixed:
                // "Маты из каменной ваты ... EI60" — огнезащита, хотя слово "маты" стоит раньше.
                if (best.Role == Role.Insulation && FireThermal.IsMatch(text))
                    return Role.FireThermalProtection;
                return best.Role;
            default: return best.Role;
        }
    }

    private static T Match<T>(List<Rule<T>> rules, string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            foreach (Rule<T> rule in rules)
            {
                if (rule.Pattern.IsMatch(text))
                    return rule.Value;
            }
        }
        return default!;
    }
}
