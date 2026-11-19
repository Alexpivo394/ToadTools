namespace ClassifierCode.Engine.Profile;

/// <summary>
///     Профиль объекта: классификатор, параметры записи, категории и правила распознавания.
///     Изменяемый POCO — редактор правит его напрямую, движок работает со скомпилированной копией.
/// </summary>
public sealed class ClassifierProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";

    /// <summary>Шаблоны имени файла модели, по которым профиль выбирается автоматически.</summary>
    public List<string> FileNamePatterns { get; set; } = [];

    public List<OutputField> Outputs { get; set; } = [];

    /// <summary>Имена BuiltInCategory, которые обрабатываются и к которым привязываются параметры.</summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>Именованные источники текста ("primaryName", "fullName"...).</summary>
    public Dictionary<string, TextSourceDef> Texts { get; set; } = new();

    /// <summary>Именованные наборы шаблонов; в шаблонах на них ссылаются как "@имя".</summary>
    public Dictionary<string, List<string>> PatternSets { get; set; } = new();

    public List<Position> Positions { get; set; } = [];
    public List<KeyTitle> Disciplines { get; set; } = [];
    public List<KeyTitle> Types { get; set; } = [];
    public Dictionary<string, DictionaryDef> Dictionaries { get; set; } = new();
    public Dictionary<string, List<RuleDef>> RuleSets { get; set; } = new();
    public Dictionary<string, TableDef> Tables { get; set; } = new();
    public List<ModeDef> Modes { get; set; } = [];

    /// <summary>Правила объекта, которые срабатывают раньше распознавания.</summary>
    public List<RuleDef> Exceptions { get; set; } = [];
}
