using System.ComponentModel;
using Newtonsoft.Json;

namespace ParamChecker.Core.Profiles;

/// <summary>Все профили пользователя — то, что хранится в profiles.json и в файлах экспорта.</summary>
public sealed class ProfileSet
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<CheckProfile> Profiles { get; set; } = [];
}

/// <summary>Профиль: модели и правила, по которым они проверяются.</summary>
public sealed class CheckProfile
{
    public string Name { get; set; } = "";

    /// <summary>Профиль участвует в запуске.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Вид по умолчанию для моделей профиля; пусто — вся модель.</summary>
    public string ViewName { get; set; } = "";

    /// <summary>РН с этим словом в имени не открываются; пусто — открываются все.</summary>
    public string WorksetKeyword { get; set; } = "";

    public List<ModelSource> Models { get; set; } = [];
    public List<CheckRule> Rules { get; set; } = [];
}

/// <summary>Модель профиля. Пустые вид и ключевое слово — берутся из профиля.</summary>
public sealed class ModelSource
{
    /// <summary>Путь на Revit Server (<c>RSN://сервер/папка/модель.rvt</c>) или к файлу.</summary>
    public string Path { get; set; } = "";

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? ViewName { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? WorksetKeyword { get; set; }

    /// <summary>Имя модели — имя файла без расширения, как в ToadExporter.</summary>
    [JsonIgnore]
    public string Name => ModelNames.FromPath(Path);
}

/// <summary>Правило: какие элементы отобрать и какие параметры у них проверить. В отчёте — отдельный лист.</summary>
public sealed class CheckRule
{
    public string Title { get; set; } = "";
    public RuleFilter Filter { get; set; } = new();
    public List<string> Parameters { get; set; } = [];
}

/// <summary>Отбор элементов правила: по категориям, по условиям на параметры или по тому и другому.</summary>
public sealed class RuleFilter
{
    /// <summary>Имена <c>BuiltInCategory</c>, например <c>OST_DuctCurves</c>.</summary>
    public List<string> Categories { get; set; } = [];

    public SelectionMode Mode { get; set; } = SelectionMode.CategoriesAndParameters;

    /// <summary>Как связаны условия верхнего уровня. Внутри группы условия всегда связаны через «И».</summary>
    public ConditionLogic Logic { get; set; } = ConditionLogic.And;

    public List<Condition> Conditions { get; set; } = [];

    [JsonIgnore]
    public bool UsesCategories => Mode != SelectionMode.ParametersOnly;

    [JsonIgnore]
    public bool UsesConditions => Mode != SelectionMode.CategoriesOnly;
}

/// <summary>Условие на параметр или группа условий (<see cref="Group" /> не null), связанных через «И».</summary>
public sealed class Condition
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Parameter { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public ConditionOperator? Operator { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? Value { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public List<Condition>? Group { get; set; }

    [JsonIgnore]
    public bool IsGroup => Group is not null;

    public static Condition Simple(string parameter, ConditionOperator op, string value = "") =>
        new() { Parameter = parameter, Operator = op, Value = value };

    public static Condition AllOf(params Condition[] conditions) => new() { Group = conditions.ToList() };
}

/// <summary>Порядок значений совпадает со старыми файлами профилей, где логика хранилась числом.</summary>
public enum SelectionMode
{
    [Description("Только категории")] CategoriesOnly,
    [Description("Только условия")] ParametersOnly,
    [Description("Категории ИЛИ условия")] CategoriesOrParameters,
    [Description("Категории И условия")] CategoriesAndParameters
}

public enum ConditionLogic
{
    [Description("Все условия (И)")] And,
    [Description("Любое условие (ИЛИ)")] Or
}

/// <summary>Первые десять значений — в порядке старых файлов профилей; новые добавляются только в конец.</summary>
public enum ConditionOperator
{
    [Description("Равно")] Equals,
    [Description("Не равно")] NotEquals,
    [Description("Больше")] GreaterThan,
    [Description("Больше или равно")] GreaterThanOrEquals,
    [Description("Меньше")] LessThan,
    [Description("Меньше или равно")] LessThanOrEquals,
    [Description("Содержит")] Contains,
    [Description("Не содержит")] NotContains,
    [Description("Параметр есть")] Exists,
    [Description("Параметра нет")] NotExists,
    [Description("Заполнен")] IsFilled,
    [Description("Не заполнен")] IsEmpty,
    [Description("Начинается с")] StartsWith,
    [Description("Заканчивается на")] EndsWith
}
