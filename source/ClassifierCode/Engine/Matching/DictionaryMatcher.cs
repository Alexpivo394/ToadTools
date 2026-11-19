using System.Text.RegularExpressions;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Patterns;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Matching;

/// <summary>Что дал словарь: результат (уже выбранный по домену), совпавший шаблон, позиция и текст совпадения.</summary>
public sealed record DictionaryHit(ResultDef Result, string Pattern, int Index, string Text);

public static class DictionaryMatcher
{
    public static DictionaryHit? Match(CompiledDictionary dictionary, string text, MatchContext ctx, ProfileRuntime runtime)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (dictionary.Source.Normalize) text = TextSourceReader.Normalize(text);

        return dictionary.Source.Mode switch
        {
            "earliest" => Earliest(dictionary, text, ctx, runtime),
            "lastToken" => LastToken(dictionary, text, ctx, runtime),
            _ => First(dictionary, text, ctx, runtime)
        };
    }

    private static DictionaryHit? First(CompiledDictionary dictionary, string text, MatchContext ctx, ProfileRuntime runtime)
    {
        foreach (var entry in dictionary.Entries)
        {
            if (!ConditionEvaluator.All(entry.When, ctx, runtime)) continue;
            for (var i = 0; i < entry.Patterns.Count; i++)
            {
                var match = Run(entry.Patterns[i], entry.Source.Patterns[i], text, ctx);
                if (match != null) return Hit(entry, i, match, ctx);
            }
        }

        return null;
    }

    /// <summary>Побеждает совпадение, стоящее в тексте раньше; при равенстве — запись выше по списку.</summary>
    private static DictionaryHit? Earliest(CompiledDictionary dictionary, string text, MatchContext ctx, ProfileRuntime runtime)
    {
        DictionaryHit? best = null;
        foreach (var entry in dictionary.Entries)
        {
            if (!ConditionEvaluator.All(entry.When, ctx, runtime)) continue;
            for (var i = 0; i < entry.Patterns.Count; i++)
            {
                var match = Run(entry.Patterns[i], entry.Source.Patterns[i], text, ctx);
                if (match != null && (best == null || match.Index < best.Index))
                    best = Hit(entry, i, match, ctx);
            }
        }

        return best;
    }

    /// <summary>Точный токен, с конца текста: в "ШФТ-СОБ-АУПС" шифр подсистемы последний.</summary>
    private static DictionaryHit? LastToken(CompiledDictionary dictionary, string text, MatchContext ctx, ProfileRuntime runtime)
    {
        var tokens = dictionary.Separators!.Split(text);
        for (var t = tokens.Length - 1; t >= 0; t--)
        {
            foreach (var entry in dictionary.Entries)
            {
                var index = IndexOf(entry.Tokens, tokens[t]);
                if (index < 0 || !ConditionEvaluator.All(entry.When, ctx, runtime)) continue;
                return new DictionaryHit(ForDomain(entry.Source.Result, ctx.EffectiveDomain), entry.Source.Patterns[index], t, tokens[t]);
            }
        }

        return null;
    }

    private static int IndexOf(IReadOnlyList<string> tokens, string token)
    {
        for (var i = 0; i < tokens.Count; i++)
            if (string.Equals(tokens[i], token, StringComparison.Ordinal))
                return i;
        return -1;
    }

    private static Match? Run(Regex regex, string pattern, string text, MatchContext ctx)
    {
        var match = SafeMatch.Match(regex, pattern, text);
        return match.Success ? match : null;
    }

    private static DictionaryHit Hit(CompiledEntry entry, int pattern, Match match, MatchContext ctx) =>
        new(ForDomain(entry.Source.Result, ctx.EffectiveDomain), entry.Source.Patterns[pattern], match.Index, match.Value);

    /// <summary>Результат для воздуховодов, если элемент в воздуховодах; для труб — во всех остальных случаях.</summary>
    public static ResultDef ForDomain(ResultDef result, NetworkDomain domain)
    {
        if (domain == NetworkDomain.Air && result.Air != null) return result.Air;
        return result.Pipe ?? result;
    }
}
