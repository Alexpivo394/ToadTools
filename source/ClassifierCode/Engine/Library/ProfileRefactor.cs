using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Library;

public enum KeyKind
{
    Discipline,
    Type,
    Position
}

/// <summary>Где в профиле используется раздел, тип или позиция, и переименование со всеми ссылками.</summary>
public static class ProfileRefactor
{
    public static IReadOnlyList<string> References(ClassifierProfile profile, KeyKind kind, string key)
    {
        var found = new List<string>();
        Walk(profile, kind, key, null, found);
        return found;
    }

    /// <summary>Переименовывает ключ (и его определение) во всём профиле. Возвращает число заменённых ссылок.</summary>
    public static int Rename(ClassifierProfile profile, KeyKind kind, string oldKey, string newKey)
    {
        var found = new List<string>();
        Walk(profile, kind, oldKey, newKey, found);
        switch (kind)
        {
            case KeyKind.Discipline:
                foreach (var d in profile.Disciplines.Where(d => d.Key == oldKey)) d.Key = newKey;
                break;
            case KeyKind.Type:
                foreach (var t in profile.Types.Where(t => t.Key == oldKey)) t.Key = newKey;
                break;
            case KeyKind.Position:
                foreach (var p in profile.Positions.Where(p => p.Code == oldKey)) p.Code = newKey;
                break;
        }

        return found.Count;
    }

    private sealed class Walker(KeyKind kind, string key, string? replacement, List<string> found)
    {
        public string Map(string value, string where)
        {
            if (value != key) return value;
            found.Add(where);
            return replacement ?? value;
        }

        public string? MapNullable(string? value, string where) => value == null ? null : Map(value, where);

        public void List(List<string>? values, string where)
        {
            if (values == null) return;
            for (var i = 0; i < values.Count; i++) values[i] = Map(values[i], where);
        }

        public void Result(ResultDef? result, string where)
        {
            if (result == null) return;
            if (kind == KeyKind.Discipline) result.Discipline = MapNullable(result.Discipline, where);
            if (kind == KeyKind.Type) result.Type = MapNullable(result.Type, where);
            if (kind == KeyKind.Position) result.Position = MapNullable(result.Position, where);
            Result(result.Pipe, where);
            Result(result.Air, where);
        }

        public void Conditions(List<ConditionDef> conditions, string where)
        {
            foreach (var condition in conditions)
            {
                if (kind == KeyKind.Type) List(condition.Type, where);
                if (kind != KeyKind.Position) List(condition.Gives, where);
            }
        }

        /// <param name="ruleKind">Каким ключам принадлежат Allow и OnlyIf в этих правилах.</param>
        public void Rules(List<RuleDef> rules, string where, KeyKind? ruleKind)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                var at = where + " › " + (rule.Title ?? "правило " + (i + 1));
                Conditions(rule.When, at);
                Result(rule.Default, at);
                foreach (var refine in rule.Refine)
                {
                    Conditions(refine.When, at);
                    Result(refine.To, at);
                }

                if (rule.Allow != null && (ruleKind == null || ruleKind == kind) && kind != KeyKind.Position)
                {
                    List(rule.Allow.Values, at);
                    if (rule.Allow.Otherwise != "drop") rule.Allow.Otherwise = Map(rule.Allow.Otherwise, at);
                }

                foreach (var step in rule.Steps)
                {
                    Result(step.Fixed, at);
                    if ((ruleKind == null || ruleKind == kind) && kind != KeyKind.Position) List(step.OnlyIf, at);
                    foreach (var refine in step.Refine)
                    {
                        Conditions(refine.When, at);
                        Result(refine.To, at);
                    }
                }
            }
        }
    }

    private static void Walk(ClassifierProfile profile, KeyKind kind, string key, string? replacement, List<string> found)
    {
        var w = new Walker(kind, key, replacement, found);

        if (kind == KeyKind.Position)
            foreach (var discipline in profile.Disciplines)
                discipline.Position = w.MapNullable(discipline.Position, "Разделы › " + discipline.Key);

        foreach (var dictionary in profile.Dictionaries)
            for (var i = 0; i < dictionary.Value.Entries.Count; i++)
            {
                var at = "Словари › " + dictionary.Key + " › запись " + (i + 1);
                w.Result(dictionary.Value.Entries[i].Result, at);
                w.Conditions(dictionary.Value.Entries[i].When, at);
            }

        foreach (var set in profile.RuleSets) w.Rules(set.Value, "Наборы правил › " + set.Key, KeyKind.Type);
        foreach (var mode in profile.Modes)
        {
            if (kind == KeyKind.Discipline) mode.DefaultDiscipline = w.MapNullable(mode.DefaultDiscipline, "Режимы › " + mode.Key);
            w.Rules(mode.DisciplineRules, "Режимы › " + mode.Key, KeyKind.Discipline);
        }

        w.Rules(profile.Exceptions, "Исключения", null);

        foreach (var table in profile.Tables)
        {
            for (var i = 0; i < table.Value.Rows.Count; i++)
            {
                var row = table.Value.Rows[i];
                var at = "Таблицы › " + table.Key + " › строка " + (i + 1);
                if (kind == KeyKind.Discipline) row.Discipline = w.Map(row.Discipline, at);
                if (kind == KeyKind.Type) row.Type = w.Map(row.Type, at);
                if (kind == KeyKind.Position) row.Position = w.Map(row.Position, at);
            }

            if (kind != KeyKind.Type) continue;
            foreach (var fallback in table.Value.Fallbacks.ToList())
            {
                var at = "Таблицы › " + table.Key + " › замены";
                w.List(fallback.Value, at);
                var newKey = w.Map(fallback.Key, at);
                if (newKey == fallback.Key) continue;
                table.Value.Fallbacks.Remove(fallback.Key);
                table.Value.Fallbacks[newKey] = fallback.Value;
            }
        }
    }
}
