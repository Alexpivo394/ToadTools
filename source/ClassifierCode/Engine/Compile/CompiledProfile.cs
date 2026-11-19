using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Compile;

/// <summary>Ошибка профиля: где (человекочитаемый путь) и что не так.</summary>
public sealed record ProfileIssue(string Path, string Message)
{
    public override string ToString() => Path + ": " + Message;
}

public sealed class CompiledRefine(IReadOnlyList<CompiledCondition> when, ResultDef to)
{
    public IReadOnlyList<CompiledCondition> When { get; } = when;
    public ResultDef To { get; } = to;
}

public sealed class CompiledStep(StepDef source, CompiledDictionary? dictionary, IReadOnlyList<CompiledRefine> refine)
{
    public StepDef Source { get; } = source;
    public CompiledDictionary? Dictionary { get; } = dictionary;
    public IReadOnlyList<CompiledRefine> Refine { get; } = refine;
}

public sealed class CompiledRule(RuleDef source, string title, IReadOnlyList<CompiledCondition> when, IReadOnlyList<CompiledStep> steps,
    IReadOnlyList<CompiledRefine> refine)
{
    public RuleDef Source { get; } = source;

    /// <summary>Название правила или «правило N».</summary>
    public string Title { get; } = title;

    public IReadOnlyList<CompiledCondition> When { get; } = when;
    public IReadOnlyList<CompiledStep> Steps { get; } = steps;
    public IReadOnlyList<CompiledRefine> Refine { get; } = refine;
}

public sealed class CompiledTable(TableDef source, Dictionary<(string Discipline, string Type), string> rows)
{
    public TableDef Source { get; } = source;
    public IReadOnlyDictionary<(string Discipline, string Type), string> Rows { get; } = rows;
}

public sealed class CompiledMode(ModeDef source, IReadOnlyList<CompiledRule> typeRules, IReadOnlyList<CompiledRule> disciplineRules,
    CompiledTable table)
{
    public ModeDef Source { get; } = source;
    public IReadOnlyList<CompiledRule> TypeRules { get; } = typeRules;
    public IReadOnlyList<CompiledRule> DisciplineRules { get; } = disciplineRules;
    public CompiledTable Table { get; } = table;
}

/// <summary>Профиль без ошибок, готовый для движка.</summary>
public sealed class CompiledProfile(ProfileRuntime runtime, IReadOnlyList<CompiledRule> exceptions, IReadOnlyDictionary<string, CompiledMode> modes)
{
    public ProfileRuntime Runtime { get; } = runtime;
    public ClassifierProfile Source => Runtime.Profile;
    public IReadOnlyList<CompiledRule> Exceptions { get; } = exceptions;
    public IReadOnlyDictionary<string, CompiledMode> Modes { get; } = modes;

    public Position? FindPosition(string code) => Source.Positions.FirstOrDefault(p => p.Code == code);

    public string DisciplineTitle(string key) => Source.Disciplines.FirstOrDefault(d => d.Key == key)?.Title ?? key;

    public string TypeTitle(string key) => Source.Types.FirstOrDefault(t => t.Key == key)?.Title ?? key;
}
