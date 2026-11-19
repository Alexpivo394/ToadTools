using System.Text.RegularExpressions;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Matching;
using ClassifierCode.Engine.Patterns;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Compile;

/// <summary>Запись словаря, готовая к сопоставлению.</summary>
public sealed class CompiledEntry(DictionaryEntryDef source, IReadOnlyList<Regex> patterns, IReadOnlyList<string> tokens,
    IReadOnlyList<CompiledCondition> when)
{
    public DictionaryEntryDef Source { get; } = source;
    public IReadOnlyList<Regex> Patterns { get; } = patterns;

    /// <summary>Для lastToken — нормализованные токены.</summary>
    public IReadOnlyList<string> Tokens { get; } = tokens;

    public IReadOnlyList<CompiledCondition> When { get; } = when;
}

public sealed class CompiledDictionary(string name, DictionaryDef source, Regex? separators, IReadOnlyList<CompiledEntry> entries)
{
    public string Name { get; } = name;
    public DictionaryDef Source { get; } = source;
    public Regex? Separators { get; } = separators;
    public IReadOnlyList<CompiledEntry> Entries { get; } = entries;
}

public sealed class CompiledCondition(ConditionDef source, Regex? matches, CompiledDictionary? dictionary)
{
    public ConditionDef Source { get; } = source;
    public Regex? Matches { get; } = matches;
    public CompiledDictionary? Dictionary { get; } = dictionary;
}

/// <summary>
///     Профиль, из которого лениво собираются регулярки, словари и условия; кэширует их.
///     Ошибки (битый шаблон, неизвестный словарь) бросаются здесь — компилятор профиля ловит их как ошибки профиля.
/// </summary>
public sealed class ProfileRuntime(ClassifierProfile profile)
{
    public const string StepSourceName = "$source";
    private const string DefaultSeparators = @"[-_.\s()]+";

    private readonly Dictionary<string, Regex> _patterns = new();
    private readonly Dictionary<string, CompiledDictionary> _dictionaries = new();
    private readonly HashSet<string> _compilingDictionaries = [];

    public ClassifierProfile Profile { get; } = profile;

    public Regex Pattern(string pattern)
    {
        if (!_patterns.TryGetValue(pattern, out var regex))
        {
            regex = PatternCompiler.Compile(pattern, Profile.PatternSets);
            _patterns[pattern] = regex;
        }

        return regex;
    }

    public CompiledDictionary Dictionary(string name)
    {
        if (_dictionaries.TryGetValue(name, out var compiled)) return compiled;
        if (!Profile.Dictionaries.TryGetValue(name, out var source))
            throw new PatternException($"Нет словаря «{name}»");
        if (!_compilingDictionaries.Add(name))
            throw new PatternException($"Словарь «{name}» ссылается сам на себя через условия");

        try
        {
            var lastToken = source.Mode == "lastToken";
            var entries = source.Entries.Select(entry => new CompiledEntry(
                entry,
                lastToken ? [] : entry.Patterns.Select(Pattern).ToList(),
                lastToken ? entry.Patterns.Select(t => source.Normalize ? TextSourceReader.Normalize(t) : t).ToList() : [],
                Conditions(entry.When))).ToList();
            var separators = lastToken ? new Regex(source.Separators ?? DefaultSeparators, PatternCompiler.Options, PatternCompiler.Timeout) : null;
            compiled = new CompiledDictionary(name, source, separators, entries);
            _dictionaries[name] = compiled;
            return compiled;
        }
        finally
        {
            _compilingDictionaries.Remove(name);
        }
    }

    public IReadOnlyList<CompiledCondition> Conditions(IEnumerable<ConditionDef> conditions) =>
        conditions.Select(c => new CompiledCondition(
            c,
            c.Matches != null ? Pattern(c.Matches) : null,
            c.Dictionary != null ? Dictionary(c.Dictionary) : null)).ToList();

    /// <summary>Текст источника у элемента контекста; "$source" — текст текущего шага. Кэшируется в контексте.</summary>
    public string ReadSource(string name, MatchContext ctx)
    {
        if (name == StepSourceName) return ctx.StepSource ?? string.Empty;
        if (ctx.TextCache.TryGetValue(name, out var text)) return text;
        text = Profile.Texts.TryGetValue(name, out var source)
            ? TextSourceReader.Read(source, ctx.Facts, Profile.Texts, Pattern)
            : string.Empty;
        ctx.TextCache[name] = text;
        return text;
    }
}
