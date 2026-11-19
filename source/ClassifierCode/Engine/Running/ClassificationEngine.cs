using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using System.Text.RegularExpressions;
using ClassifierCode.Engine.Matching;
using ClassifierCode.Engine.Patterns;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Running;

/// <summary>
///     Классификация по профилю в одном режиме: исключения → тип → раздел → позиция таблицы.
///     Один экземпляр на прогон: кэширует тип и раздел каждого элемента (их берут вложенные и изоляция).
/// </summary>
public sealed class ClassificationEngine
{
    public const string StageExceptions = "Исключения";
    public const string StageType = "Тип";
    public const string StageDiscipline = "Раздел";
    public const string StagePosition = "Позиция";

    private const int TraceTextLength = 80;

    private readonly CompiledProfile _profile;
    private readonly CompiledMode _mode;
    private readonly ProfileRuntime _runtime;
    private readonly Dictionary<long, StageResult?> _types = new();
    private readonly Dictionary<(long, string?), StageResult?> _disciplines = new();
    private readonly Dictionary<long, StageResult?> _exceptions = new();
    private readonly Dictionary<long, MatchContext> _contexts = new();
    private readonly List<string> _warnings = [];

    public ClassificationEngine(CompiledProfile profile, string modeKey)
    {
        _profile = profile;
        _runtime = profile.Runtime;
        if (!profile.Modes.TryGetValue(modeKey, out var mode))
            throw new ArgumentException($"В профиле нет режима «{modeKey}»", nameof(modeKey));
        _mode = mode;
    }

    public string ModeKey => _mode.Source.Key;

    /// <summary>Результат стадии и строки трассы, которые к нему привели.</summary>
    private sealed record StageResult(ResultDef Result, IReadOnlyList<string> Trace);

    private enum Stage
    {
        Type,
        Discipline
    }

    public EngineResult Classify(IElementFacts facts)
    {
        try
        {
            return ClassifyCore(facts);
        }
        catch (Exception exception) when (exception is PatternTimeoutException or RegexMatchTimeoutException)
        {
            var reason = exception is PatternTimeoutException ? exception.Message : "шаблон проверялся слишком долго — упростите его";
            _warnings.Add(reason);
            return new EngineResult(EngineStatus.Unclassified, null, null, null, reason, [new TraceStep(StagePosition, reason)]);
        }
    }

    private EngineResult ClassifyCore(IElementFacts facts)
    {
        var trace = new List<TraceStep>();
        var ctx = Context(facts);
        string? presetType = null;
        string? presetDiscipline = null;

        var exception = ExceptionOf(facts);
        if (exception != null)
        {
            trace.AddRange(exception.Trace.Select(t => new TraceStep(StageExceptions, t)));
            var result = exception.Result;
            if (result.Skip != null)
                return new EngineResult(EngineStatus.Skipped, null, null, null, result.Skip, trace);
            if (result.Position != null)
                return Done(null, null, result.Position, trace);
            presetType = result.Type;
            presetDiscipline = result.Discipline;
        }

        var type = presetType;
        if (type == null)
        {
            var typeResult = TypeOf(facts);
            if (typeResult != null) trace.AddRange(typeResult.Trace.Select(t => new TraceStep(StageType, t)));
            if (typeResult?.Result.Skip != null)
                return new EngineResult(EngineStatus.Skipped, null, null, null, typeResult.Result.Skip, trace);
            type = typeResult?.Result.Type;
        }

        if (type == null)
            return Unclassified(null, null, "не определён тип элемента", trace);

        var discipline = presetDiscipline;
        if (discipline == null)
        {
            var disciplineResult = DisciplineOf(facts, type);
            if (disciplineResult != null) trace.AddRange(disciplineResult.Trace.Select(t => new TraceStep(StageDiscipline, t)));
            if (disciplineResult?.Result.Skip != null)
                return new EngineResult(EngineStatus.Skipped, null, type, null, disciplineResult.Result.Skip, trace);
            discipline = disciplineResult?.Result.Discipline;
        }

        if (discipline == null)
            return Unclassified(null, type, "не определён раздел", trace);

        var code = Lookup(discipline, type, trace);
        if (code == null)
            return Unclassified(discipline, type,
                $"нет позиции для «{_profile.DisciplineTitle(discipline)}» + «{_profile.TypeTitle(type)}»", trace);

        return Done(discipline, type, code, trace);
    }

    private EngineResult Done(string? discipline, string? type, string code, List<TraceStep> trace)
    {
        var position = _profile.FindPosition(code);
        if (position == null)
            return Unclassified(discipline, type, $"нет позиции «{code}» в классификаторе", trace);
        trace.Add(new TraceStep(StagePosition, position.Code + " " + position.Description));
        return new EngineResult(EngineStatus.Classified, discipline, type, position, null, trace);
    }

    private static EngineResult Unclassified(string? discipline, string? type, string reason, List<TraceStep> trace)
    {
        trace.Add(new TraceStep(StagePosition, reason));
        return new EngineResult(EngineStatus.Unclassified, discipline, type, null, reason, trace);
    }

    /// <summary>Точная строка → замены типа по порядку (без транзитивности) → строка «*».</summary>
    private string? Lookup(string discipline, string type, List<TraceStep> trace)
    {
        var rows = _mode.Table.Rows;
        if (rows.TryGetValue((discipline, type), out var code)) return code;

        if (_mode.Table.Source.Fallbacks.TryGetValue(type, out var alternatives))
            foreach (var alternative in alternatives)
                if (rows.TryGetValue((discipline, alternative), out code))
                {
                    trace.Add(new TraceStep(StagePosition, $"нет строки для «{_profile.TypeTitle(type)}», взята замена «{_profile.TypeTitle(alternative)}»"));
                    return code;
                }

        if (rows.TryGetValue(("*", type), out code))
        {
            trace.Add(new TraceStep(StagePosition, "взята строка для любого раздела"));
            return code;
        }

        return null;
    }

    private MatchContext Context(IElementFacts facts)
    {
        if (!_contexts.TryGetValue(facts.Id, out var ctx))
        {
            var domain = facts.Domain == NetworkDomain.Other && _mode.Source.UnconnectedAsAir ? NetworkDomain.Air : facts.Domain;
            ctx = new MatchContext(facts, _mode.Source.Key, domain);
            _contexts[facts.Id] = ctx;
        }

        return ctx;
    }

    /// <summary>Предупреждения прогона (шаблоны, не уложившиеся в таймаут), по всем элементам.</summary>
    public IEnumerable<string> Warnings => _warnings.Distinct();

    /// <summary>Исключение, сработавшее для элемента (тоже кэшируется: его тип и раздел видят вложенные элементы).</summary>
    private StageResult? ExceptionOf(IElementFacts facts) =>
        Cached(_exceptions, facts.Id, () => RunRules(_profile.Exceptions, facts, Context(facts), null));

    private StageResult? TypeOf(IElementFacts facts) =>
        Cached(_types, facts.Id, () => RunRules(_mode.TypeRules, facts, Context(facts), Stage.Type));

    /// <summary>Раздел зависит от типа (правила раздела смотрят на тип), поэтому кэш — по элементу и типу.</summary>
    private StageResult? DisciplineOf(IElementFacts facts, string? type) =>
        Cached(_disciplines, (facts.Id, type), () =>
        {
            var ctx = Context(facts) with { TypeKey = type };
            var result = RunRules(_mode.DisciplineRules, facts, ctx, Stage.Discipline);
            if (result == null && _mode.Source.DefaultDiscipline != null)
                result = new StageResult(new ResultDef { Discipline = _mode.Source.DefaultDiscipline },
                    [$"раздел по умолчанию: {_profile.DisciplineTitle(_mode.Source.DefaultDiscipline)}"]);
            return result;
        });

    /// <summary>Кэш стадии с защитой от циклов родитель ↔ хост; при ошибке заглушка цикла не остаётся в кэше.</summary>
    private static StageResult? Cached<TKey>(Dictionary<TKey, StageResult?> cache, TKey key, Func<StageResult?> compute) where TKey : notnull
    {
        if (cache.TryGetValue(key, out var cached)) return cached;
        cache[key] = null;
        try
        {
            var result = compute();
            cache[key] = result;
            return result;
        }
        catch
        {
            cache.Remove(key);
            throw;
        }
    }

    /// <summary>Тип родителя/хоста — как у него самого: из исключения, если оно задало тип, иначе по правилам.</summary>
    private string? OwnType(IElementFacts facts) => ExceptionOf(facts)?.Result.Type ?? TypeOf(facts)?.Result.Type;

    /// <summary>Стадия родителя/хоста с учётом его исключения (заданный там тип или раздел).</summary>
    private StageResult? StageOf(IElementFacts target, Stage stage)
    {
        var preset = ExceptionOf(target);
        if (stage == Stage.Type)
            return preset?.Result.Type != null ? preset : TypeOf(target);
        return preset?.Result.Discipline != null ? preset : DisciplineOf(target, OwnType(target));
    }

    /// <param name="stage">null — исключения: наследование там не используется.</param>
    private StageResult? RunRules(IReadOnlyList<CompiledRule> rules, IElementFacts facts, MatchContext ctx, Stage? stage)
    {
        foreach (var rule in rules)
        {
            if (!ConditionEvaluator.All(rule.When, ctx, _runtime)) continue;

            for (var s = 0; s < rule.Steps.Count; s++)
            {
                var step = rule.Steps[s];
                var outcome = RunStep(step, facts, ctx, stage);
                if (outcome == null) continue;

                var (result, sourceText, note) = outcome.Value;
                var trace = new List<string> { $"{rule.Title}, шаг {s + 1}: {note}" };

                result = ApplyRefine(step.Refine, result, ctx, sourceText, stage, trace);
                result = ApplyAllow(rule.Source.Allow, result, stage, trace);
                if (result == null) continue;
                result = ApplyRefine(rule.Refine, result, ctx, sourceText, stage, trace);
                return new StageResult(result, trace);
            }

            if (rule.Source.Default != null)
                return new StageResult(DictionaryMatcher.ForDomain(rule.Source.Default, ctx.EffectiveDomain),
                    [$"{rule.Title}: {Describe(rule.Source.Default)}"]);
            if (!rule.Source.Fallthrough) return null;
        }

        return null;
    }

    private (ResultDef Result, string? SourceText, string Note)? RunStep(CompiledStep step, IElementFacts facts, MatchContext ctx, Stage? stage)
    {
        var def = step.Source;
        if (def.Fixed != null)
            return (DictionaryMatcher.ForDomain(def.Fixed, ctx.EffectiveDomain), null, Describe(def.Fixed));

        if (def.Inherit != null)
        {
            if (stage == null) return null;
            var target = def.Inherit == "host" ? facts.Host : facts.Parent;
            if (target == null) return null;
            var inherited = StageOf(target, stage.Value);
            if (inherited == null) return null;
            if (def.OnlyIf.Count > 0 && !def.OnlyIf.Contains(ConditionEvaluator.ResultKey(inherited.Result))) return null;
            var who = def.Inherit == "host" ? "изолируемого элемента" : "родительского семейства";
            return (inherited.Result, null, $"как у {who}: {Describe(inherited.Result)}");
        }

        if (step.Dictionary == null) return null;
        var sources = def.Sources.Count > 0 ? def.Sources : def.Source != null ? [def.Source] : new List<string>();
        var text = string.Join(TextSourceReader.Separator, sources.Select(s => _runtime.ReadSource(s, ctx)));
        var hit = DictionaryMatcher.Match(step.Dictionary, text, ctx, _runtime);
        if (hit == null) return null;

        var result = hit.Result;
        if (result.Skip != null && result.Skip.Contains("{match}"))
            result = new ResultDef { Skip = result.Skip.Replace("{match}", hit.Text) };
        var note = $"{string.Join(" + ", sources)} «{Cut(text)}» → словарь «{step.Dictionary.Name}», «{hit.Pattern}» → {Describe(result)}";
        return (result, text, note);
    }

    private ResultDef ApplyRefine(IReadOnlyList<CompiledRefine> refines, ResultDef result, MatchContext ctx, string? sourceText,
        Stage? stage, List<string> trace)
    {
        if (refines.Count == 0) return result;
        var refineCtx = ctx with { StepSource = sourceText, TypeKey = stage == Stage.Type ? result.Type : ctx.TypeKey };
        foreach (var refine in refines)
        {
            if (!ConditionEvaluator.All(refine.When, refineCtx, _runtime)) continue;
            var refined = DictionaryMatcher.ForDomain(refine.To, ctx.EffectiveDomain);
            trace.Add($"уточнение: {Describe(result)} → {Describe(refined)}");
            return refined;
        }

        return result;
    }

    private ResultDef? ApplyAllow(AllowDef? allow, ResultDef result, Stage? stage, List<string> trace)
    {
        if (allow == null) return result;
        // «Пропустить» — не значение, а решение: список допустимых его не касается.
        if (result.Skip != null) return result;
        var key = ConditionEvaluator.ResultKey(result);
        if (allow.Values.Contains(key)) return result;
        if (allow.Otherwise == "drop")
        {
            trace.Add($"{Describe(result)} здесь не подходит — дальше");
            return null;
        }

        var replaced = stage == Stage.Discipline ? new ResultDef { Discipline = allow.Otherwise } : new ResultDef { Type = allow.Otherwise };
        trace.Add($"{Describe(result)} здесь не подходит → {Describe(replaced)}");
        return replaced;
    }

    private string Describe(ResultDef result)
    {
        if (result.Skip != null) return "пропустить: " + result.Skip;
        if (result.Position != null) return "позиция " + result.Position;
        if (result.Type != null) return "тип «" + _profile.TypeTitle(result.Type) + "»";
        if (result.Discipline != null) return "раздел «" + _profile.DisciplineTitle(result.Discipline) + "»";
        return "—";
    }

    private static string Cut(string text)
    {
        text = text.Trim();
        return text.Length <= TraceTextLength ? text : text.Substring(0, TraceTextLength) + "…";
    }
}
