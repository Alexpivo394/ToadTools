using System.Text.RegularExpressions;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Patterns;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Matching;

/// <summary>Условия правил, записей словаря и исключений: все через И, у каждого — флаг НЕ.</summary>
public static class ConditionEvaluator
{
    public static bool All(IReadOnlyList<CompiledCondition> conditions, MatchContext ctx, ProfileRuntime runtime)
    {
        foreach (var condition in conditions)
            if (Holds(condition, ctx, runtime) == condition.Source.Not)
                return false;
        return true;
    }

    private static bool Holds(CompiledCondition condition, MatchContext ctx, ProfileRuntime runtime)
    {
        var def = condition.Source;
        if (def.Category != null && !def.Category.Contains(ctx.Facts.Category)) return false;
        if (def.Domain != null && !string.Equals(def.Domain, DomainKey(ctx.EffectiveDomain), StringComparison.OrdinalIgnoreCase)) return false;
        if (def.Mode != null && !def.Mode.Contains(ctx.ModeKey)) return false;
        if (def.Type != null && (ctx.TypeKey == null || !def.Type.Contains(ctx.TypeKey))) return false;

        if (def.Source == null) return true;
        var text = runtime.ReadSource(def.Source, ctx);

        if (condition.Matches != null && !IsMatch(condition.Matches, def.Matches!, text, ctx)) return false;

        if (condition.Dictionary != null)
        {
            var hit = DictionaryMatcher.Match(condition.Dictionary, text, ctx, runtime);
            if (def.GivesNothing) return hit == null;
            if (def.Gives != null) return hit != null && def.Gives.Contains(ResultKey(hit.Result));
            return hit != null;
        }

        return true;
    }

    private static bool IsMatch(Regex regex, string pattern, string text, MatchContext ctx)
    {
        return SafeMatch.IsMatch(regex, pattern, text);
    }

    public static string DomainKey(NetworkDomain domain) => domain switch
    {
        NetworkDomain.Pipe => "pipe",
        NetworkDomain.Air => "air",
        _ => "other"
    };

    /// <summary>Ключ результата для сравнения: тип, раздел, позиция или "skip".</summary>
    public static string ResultKey(ResultDef result) =>
        result.Type ?? result.Discipline ?? result.Position ?? (result.Skip != null ? "skip" : string.Empty);
}
