using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Library;

/// <summary>Что профиль может выдать: достижимые типы и разделы и сочетания, для которых в таблице нет позиции.</summary>
public static class ProfileAnalysis
{
    public static IReadOnlyCollection<string> ReachableTypes(ClassifierProfile profile, string ruleSet)
    {
        var types = new HashSet<string>();
        if (profile.RuleSets.TryGetValue(ruleSet, out var rules)) Collect(profile, rules, r => r.Type, types);
        Collect(profile, profile.Exceptions, r => r.Type, types);
        return types;
    }

    public static IReadOnlyCollection<string> ReachableDisciplines(ClassifierProfile profile, ModeDef mode)
    {
        var disciplines = new HashSet<string>();
        Collect(profile, mode.DisciplineRules, r => r.Discipline, disciplines);
        Collect(profile, profile.Exceptions, r => r.Discipline, disciplines);
        if (mode.DefaultDiscipline != null) disciplines.Add(mode.DefaultDiscipline);
        return disciplines;
    }

    /// <summary>Сочетания «раздел + тип», которые могут прийти, но не дают позиции (ни строки, ни замены, ни «*»).</summary>
    public static IReadOnlyList<(string Discipline, string Type)> Gaps(ClassifierProfile profile, ModeDef mode)
    {
        if (!profile.Tables.TryGetValue(mode.Table, out var table)) return [];
        var rows = new HashSet<(string, string)>(table.Rows.Select(r => (r.Discipline, r.Type)));
        var gaps = new List<(string, string)>();
        foreach (var discipline in ReachableDisciplines(profile, mode).OrderBy(d => d))
        foreach (var type in ReachableTypes(profile, mode.TypeRules).OrderBy(t => t))
        {
            if (rows.Contains((discipline, type)) || rows.Contains(("*", type))) continue;
            if (table.Fallbacks.TryGetValue(type, out var alternatives) && alternatives.Any(a => rows.Contains((discipline, a)))) continue;
            gaps.Add((discipline, type));
        }

        return gaps;
    }

    private static void Collect(ClassifierProfile profile, IEnumerable<RuleDef> rules, Func<ResultDef, string?> key, HashSet<string> into)
    {
        void Add(ResultDef? result, AllowDef? allow)
        {
            if (result == null) return;
            if (key(result) is { } value && (allow == null || allow.Values.Contains(value))) into.Add(value);
            Add(result.Pipe, allow);
            Add(result.Air, allow);
        }

        foreach (var rule in rules)
        {
            Add(rule.Default, null);
            foreach (var refine in rule.Refine) Add(refine.To, null);
            if (rule.Allow is { Otherwise: not "drop" } allow) into.Add(allow.Otherwise);
            foreach (var step in rule.Steps)
            {
                Add(step.Fixed, rule.Allow);
                foreach (var refine in step.Refine) Add(refine.To, rule.Allow);
                if (step.Dictionary != null && profile.Dictionaries.TryGetValue(step.Dictionary, out var dictionary))
                    foreach (var entry in dictionary.Entries)
                        Add(entry.Result, rule.Allow);
            }
        }
    }
}
