namespace ClassifierCode.Engine.Profile;

/// <summary>Куда записывается поле позиции: "code", "description" или имя доп. колонки.</summary>
public sealed class OutputField
{
    public string Field { get; set; } = string.Empty;

    /// <summary>Имя параметра; пустое — поле не записывается.</summary>
    public string Parameter { get; set; } = string.Empty;

    /// <summary>GUID общего параметра — для подгрузки из ФОП и однозначного поиска.</summary>
    public Guid? Guid { get; set; }
}

/// <summary>Позиция классификатора.</summary>
public sealed class Position
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, string> Extra { get; set; } = new();
}

/// <summary>Раздел или тип элемента: ключ для правил, название для людей.</summary>
public sealed class KeyTitle
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>Код позиции раздела (для отчёта и подсказок).</summary>
    public string? Position { get; set; }
}

/// <summary>Склейка частей элемента через " | ".</summary>
public sealed class TextSourceDef
{
    public List<string> Parts { get; set; } = [];
    public bool Lowercase { get; set; }

    /// <summary>Верхний регистр и латиница-двойник → кириллица.</summary>
    public bool Normalize { get; set; }

    /// <summary>Именованный источник, который читается, если этот пуст.</summary>
    public string? Fallback { get; set; }
}

public sealed class DictionaryDef
{
    /// <summary>"first", "earliest" или "lastToken".</summary>
    public string Mode { get; set; } = "first";

    public bool Normalize { get; set; }

    /// <summary>Разделители токенов (регулярка) для "lastToken".</summary>
    public string? Separators { get; set; }

    public List<DictionaryEntryDef> Entries { get; set; } = [];
}

public sealed class DictionaryEntryDef
{
    public ResultDef Result { get; set; } = new();

    /// <summary>Шаблоны; для "lastToken" — точные токены.</summary>
    public List<string> Patterns { get; set; } = [];

    public List<ConditionDef> When { get; set; } = [];
}

/// <summary>Результат: раздел, тип, позиция или пропуск; либо отдельно для труб и воздуховодов.</summary>
public sealed class ResultDef
{
    public string? Discipline { get; set; }
    public string? Type { get; set; }
    public string? Position { get; set; }

    /// <summary>Причина пропуска; "{match}" заменяется совпавшим текстом.</summary>
    public string? Skip { get; set; }

    public ResultDef? Pipe { get; set; }
    public ResultDef? Air { get; set; }
}

public sealed class RuleDef
{
    public string? Title { get; set; }
    public List<ConditionDef> When { get; set; } = [];
    public List<StepDef> Steps { get; set; } = [];
    public AllowDef? Allow { get; set; }
    public List<RefineDef> Refine { get; set; } = [];
    public ResultDef? Default { get; set; }

    /// <summary>Нет результата — перейти к следующему правилу, а не остановиться.</summary>
    public bool Fallthrough { get; set; }
}

public sealed class StepDef
{
    /// <summary>"parent" или "host".</summary>
    public string? Inherit { get; set; }

    /// <summary>Для inherit: брать только эти результаты.</summary>
    public List<string> OnlyIf { get; set; } = [];

    public string? Dictionary { get; set; }
    public string? Source { get; set; }

    /// <summary>Несколько источников, склеенных через " | ".</summary>
    public List<string> Sources { get; set; } = [];

    public List<RefineDef> Refine { get; set; } = [];
    public ResultDef? Fixed { get; set; }
}

/// <summary>Одно условие; задаётся ровно один вид. Not — инверсия.</summary>
public sealed class ConditionDef
{
    public bool Not { get; set; }
    public List<string>? Category { get; set; }

    /// <summary>"pipe", "air" или "other".</summary>
    public string? Domain { get; set; }

    public List<string>? Mode { get; set; }
    public List<string>? Type { get; set; }

    /// <summary>Источник текста; "$source" — текст, на котором сработал шаг.</summary>
    public string? Source { get; set; }

    public string? Matches { get; set; }
    public string? Dictionary { get; set; }
    public List<string>? Gives { get; set; }
    public bool GivesNothing { get; set; }
}

public sealed class RefineDef
{
    public List<ConditionDef> When { get; set; } = [];
    public ResultDef To { get; set; } = new();
}

public sealed class AllowDef
{
    public List<string> Values { get; set; } = [];

    /// <summary>"drop" — шаг ничего не дал, иначе ключ замены.</summary>
    public string Otherwise { get; set; } = "drop";
}

public sealed class TableDef
{
    public List<TableRow> Rows { get; set; } = [];

    /// <summary>Тип → замены по порядку, если для типа нет строки в разделе.</summary>
    public Dictionary<string, List<string>> Fallbacks { get; set; } = new();
}

public sealed class TableRow
{
    /// <summary>Ключ раздела или "*" — для любого раздела.</summary>
    public string Discipline { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
}

/// <summary>Раздел модели, который выбирается в окне запуска.</summary>
public sealed class ModeDef
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Hint { get; set; } = string.Empty;
    public string ReportMode { get; set; } = string.Empty;
    public List<string> FileTokens { get; set; } = [];

    /// <summary>Неподключённые элементы считать воздуховодами.</summary>
    public bool UnconnectedAsAir { get; set; }

    public string TypeRules { get; set; } = string.Empty;
    public List<RuleDef> DisciplineRules { get; set; } = [];
    public string? DefaultDiscipline { get; set; }
    public string Table { get; set; } = string.Empty;
}
