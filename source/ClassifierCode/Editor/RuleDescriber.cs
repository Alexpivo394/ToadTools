using ClassifierCode.Editor.ViewModels;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Editor;

/// <summary>Правило профиля — по-русски, чтобы логику можно было прочитать без JSON.</summary>
public static class RuleDescriber
{
    public static IReadOnlyList<string> Describe(RuleDef rule, ClassifierProfile profile, bool isTypeStage)
    {
        var lines = new List<string>();
        var head = string.IsNullOrWhiteSpace(rule.Title) ? "Правило" : rule.Title!;
        lines.Add(rule.When.Count > 0 ? $"{head}. Если {Conditions(rule.When, profile)}:" : $"{head}. Для всех элементов:");

        for (var i = 0; i < rule.Steps.Count; i++)
            lines.Add($"  {i + 1}) {Step(rule.Steps[i], profile, isTypeStage)}");
        if (rule.Allow != null)
            lines.Add("  берутся только " + string.Join(", ", rule.Allow.Values.Select(v => "«" + Title(v, profile) + "»")) +
                      (rule.Allow.Otherwise == "drop" ? "; остальное пропускается" : $"; остальное → «{Title(rule.Allow.Otherwise, profile)}»"));
        foreach (var refine in rule.Refine)
            lines.Add($"  уточнение: если {Conditions(refine.When, profile)} → {Result(refine.To, profile)}");
        if (rule.Default != null)
            lines.Add((rule.Steps.Count > 0 ? "  иначе → " : "  → ") + Result(rule.Default, profile));
        lines.Add(rule.Fallthrough ? "  ничего не нашлось — дальше, к следующим правилам" : "  на этом правиле поиск заканчивается");
        return lines;
    }

    private static string Step(StepDef step, ClassifierProfile profile, bool isTypeStage)
    {
        if (step.Fixed != null) return Result(step.Fixed, profile);
        if (step.Inherit != null)
        {
            var what = isTypeStage ? "тип" : "раздел";
            var who = step.Inherit == "host" ? "изолируемого элемента" : "родительского семейства";
            var only = step.OnlyIf.Count > 0 ? ", если это " + string.Join(", ", step.OnlyIf.Select(k => "«" + Title(k, profile) + "»")) : string.Empty;
            return $"{what} — как у {who}{only}";
        }

        var sources = step.Sources.Count > 0 ? step.Sources : step.Source != null ? [step.Source] : new List<string>();
        var refine = step.Refine.Count > 0 ? " (с уточнениями)" : string.Empty;
        return $"словарь «{step.Dictionary}» по «{string.Join(" + ", sources)}»{refine}";
    }

    public static string Conditions(IEnumerable<ConditionDef> conditions, ClassifierProfile profile) =>
        string.Join(" и ", conditions.Select(c => Condition(c, profile)));

    private static string Condition(ConditionDef c, ClassifierProfile profile)
    {
        var not = c.Not ? "не " : string.Empty;
        if (c.Category != null) return $"{not}категория — {string.Join(", ", c.Category.Select(CategoryTitle))}";
        if (c.Mode != null) return $"{not}режим — {string.Join(", ", c.Mode.Select(m => profile.Modes.FirstOrDefault(x => x.Key == m)?.Title ?? m))}";
        if (c.Type != null) return $"{not}тип — {string.Join(", ", c.Type.Select(t => Title(t, profile)))}";
        if (c.Domain != null) return $"{not}сеть — {(c.Domain == "air" ? "воздуховоды" : c.Domain == "pipe" ? "трубы" : "прочее")}";
        if (c.Source != null && c.Matches != null) return $"«{SourceTitle(c.Source)}» {(c.Not ? "не " : string.Empty)}содержит «{c.Matches}»";
        if (c.Source != null && c.Dictionary != null)
        {
            var result = c.GivesNothing ? "ничего не дал" : c.Gives != null ? "дал " + string.Join(", ", c.Gives.Select(g => "«" + Title(g, profile) + "»")) : "что-то дал";
            return $"{not}словарь «{c.Dictionary}» по «{SourceTitle(c.Source)}» {result}";
        }

        return "всегда";
    }

    private static string SourceTitle(string source) => source switch
    {
        "$source" => "текст шага",
        _ when source.StartsWith("param:") => "параметр " + source.Substring(6),
        _ => source
    };

    public static string Result(ResultDef result, ClassifierProfile profile)
    {
        if (result.Skip != null) return "пропустить: " + result.Skip;
        if (result.Position != null)
            return "позиция " + result.Position + (profile.Positions.FirstOrDefault(p => p.Code == result.Position) is { } p ? " " + p.Description : string.Empty);
        if (result.Type != null) return "тип «" + Title(result.Type, profile) + "»";
        if (result.Discipline != null) return "раздел «" + Title(result.Discipline, profile) + "»";
        var parts = new List<string>();
        if (result.Pipe != null) parts.Add("для труб " + Result(result.Pipe, profile));
        if (result.Air != null) parts.Add("для воздуховодов " + Result(result.Air, profile));
        return string.Join(", ", parts);
    }

    private static string Title(string key, ClassifierProfile profile) =>
        profile.Types.FirstOrDefault(t => t.Key == key)?.Title ?? profile.Disciplines.FirstOrDefault(d => d.Key == key)?.Title ?? key;

    public static string CategoryTitle(string name) => CategoryCatalog.All.FirstOrDefault(c => c.Name == name).Title ?? name;
}
