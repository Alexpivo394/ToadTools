using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Patterns;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Compile;

/// <summary>
///     Проверяет профиль и собирает его для движка. Любая ошибка — профиль не собирается:
///     лучше не дать запустить, чем молча получить «не классифицировано».
/// </summary>
public static class ProfileCompiler
{
    private const string Sep = " › ";

    /// <summary>
    ///     Проверка имён категорий (Revit-часть знает, какие BuiltInCategory есть в этой версии). Задаётся один раз
    ///     при запуске плагина; null — имена не проверяются.
    /// </summary>
    public static Func<string, bool>? KnownCategory { get; set; }

    public static (CompiledProfile? Profile, IReadOnlyList<ProfileIssue> Issues) Compile(ClassifierProfile profile) =>
        Compile(profile, KnownCategory);

    public static (CompiledProfile? Profile, IReadOnlyList<ProfileIssue> Issues) Compile(ClassifierProfile profile, Func<string, bool>? knownCategory)
    {
        var builder = new Builder(profile, knownCategory);
        var compiled = builder.Build();
        return builder.Issues.Count == 0 ? (compiled, builder.Issues) : (null, builder.Issues);
    }

    private sealed class Builder(ClassifierProfile profile, Func<string, bool>? knownCategory)
    {
        private readonly ProfileRuntime _runtime = new(profile);
        private readonly HashSet<string> _disciplines = new(profile.Disciplines.Select(d => d.Key));
        private readonly HashSet<string> _types = new(profile.Types.Select(t => t.Key));
        private readonly HashSet<string> _positions = new(profile.Positions.Select(p => p.Code));

        public List<ProfileIssue> Issues { get; } = [];

        private void Issue(string path, string message) => Issues.Add(new ProfileIssue(path, message));

        public CompiledProfile Build()
        {
            CheckOutputs();
            if (knownCategory != null)
                foreach (var category in profile.Categories.Where(c => !knownCategory(c)))
                    Issue("Категории", $"категории «{category}» нет в этой версии Revit");
            CheckDuplicates("Разделы", profile.Disciplines.Select(d => d.Key));
            CheckDuplicates("Типы", profile.Types.Select(t => t.Key));
            CheckDuplicates("Классификатор", profile.Positions.Select(p => p.Code));
            CheckDuplicates("Режимы", profile.Modes.Select(m => m.Key));
            foreach (var discipline in profile.Disciplines)
                if (discipline.Position != null && !_positions.Contains(discipline.Position))
                    Issue("Разделы" + Sep + discipline.Key, $"нет позиции «{discipline.Position}» в классификаторе");

            CheckTexts();
            foreach (var set in profile.PatternSets)
                for (var i = 0; i < set.Value.Count; i++)
                    Try("Наборы шаблонов" + Sep + set.Key + Sep + "шаблон " + (i + 1), () => _runtime.Pattern(set.Value[i]));
            foreach (var dictionary in profile.Dictionaries) CheckDictionary(dictionary.Key, dictionary.Value);

            var ruleSets = new Dictionary<string, IReadOnlyList<CompiledRule>>();
            foreach (var set in profile.RuleSets)
                ruleSets[set.Key] = Rules("Наборы правил" + Sep + set.Key, set.Value, RuleKind.Type);

            var tables = new Dictionary<string, CompiledTable>();
            foreach (var table in profile.Tables) tables[table.Key] = Table(table.Key, table.Value);

            var modes = new Dictionary<string, CompiledMode>();
            foreach (var mode in profile.Modes)
            {
                var path = "Режимы" + Sep + mode.Key;
                if (!ruleSets.TryGetValue(mode.TypeRules, out var typeRules))
                {
                    Issue(path, $"нет набора правил типов «{mode.TypeRules}»");
                    typeRules = [];
                }

                if (!tables.TryGetValue(mode.Table, out var table))
                {
                    Issue(path, $"нет таблицы «{mode.Table}»");
                    table = new CompiledTable(new TableDef(), new Dictionary<(string, string), string>());
                }

                if (mode.DefaultDiscipline != null && !_disciplines.Contains(mode.DefaultDiscipline))
                    Issue(path, $"раздел по умолчанию «{mode.DefaultDiscipline}» не задан в разделах");

                var disciplineRules = Rules(path + Sep + "правила раздела", mode.DisciplineRules, RuleKind.Discipline);
                modes[mode.Key] = new CompiledMode(mode, typeRules, disciplineRules, table);
            }

            for (var i = 0; i < profile.Exceptions.Count; i++)
            {
                var rule = profile.Exceptions[i];
                if (rule.When.Count == 0)
                    Issue("Исключения" + Sep + (string.IsNullOrWhiteSpace(rule.Title) ? "правило " + (i + 1) : rule.Title), "исключение без условий срабатывает на все элементы — добавьте условие");
            }

            var exceptions = Rules("Исключения", profile.Exceptions, RuleKind.Exception);
            return new CompiledProfile(_runtime, exceptions, modes);
        }

        private void CheckOutputs()
        {
            if (!profile.Outputs.Any(o => o.Field == "code" && !string.IsNullOrWhiteSpace(o.Parameter)))
                Issue("Параметры", "не задан параметр для кода (поле «code»)");
        }

        private void CheckDuplicates(string path, IEnumerable<string> keys)
        {
            foreach (var group in keys.GroupBy(k => k).Where(g => g.Count() > 1))
                Issue(path, $"«{group.Key}» встречается {group.Count()} раза");
            foreach (var _ in keys.Where(string.IsNullOrWhiteSpace).Take(1))
                Issue(path, "есть пустой ключ");
        }

        private void CheckTexts()
        {
            foreach (var text in profile.Texts)
            {
                var path = "Тексты" + Sep + text.Key;
                foreach (var part in text.Value.Parts)
                {
                    if (TextSourceReader.SimpleParts.Contains(part)) continue;
                    var prefix = TextSourceReader.PrefixedParts.FirstOrDefault(p => part.StartsWith(p, StringComparison.Ordinal));
                    if (prefix == null)
                    {
                        Issue(path, $"неизвестная часть «{part}»");
                        continue;
                    }

                    var argument = part.Substring(prefix.Length);
                    if (prefix == "firstStringMatching:") Try(path, () => _runtime.Pattern(argument));
                    else if (prefix == "text:" && !profile.Texts.ContainsKey(argument)) Issue(path, $"нет источника «{argument}»");
                }

                if (text.Value.Fallback != null && !profile.Texts.ContainsKey(text.Value.Fallback))
                    Issue(path, $"нет источника «{text.Value.Fallback}» для замены пустого");
            }
        }

        private void CheckDictionary(string name, DictionaryDef dictionary)
        {
            var path = "Словари" + Sep + name;
            if (dictionary.Mode is not ("first" or "earliest" or "lastToken"))
                Issue(path, $"неизвестный режим «{dictionary.Mode}»");
            for (var i = 0; i < dictionary.Entries.Count; i++)
            {
                var entry = dictionary.Entries[i];
                var entryPath = path + Sep + "запись " + (i + 1);
                CheckResult(entryPath, entry.Result);
                CheckConditions(entryPath, entry.When);
                if (dictionary.Mode != "lastToken")
                    foreach (var pattern in entry.Patterns)
                        Try(entryPath, () => _runtime.Pattern(pattern));
            }

            Try(path, () => _runtime.Dictionary(name));
        }

        private enum RuleKind
        {
            Type,
            Discipline,
            Exception
        }

        private IReadOnlyList<CompiledRule> Rules(string path, List<RuleDef> rules, RuleKind kind)
        {
            var compiled = new List<CompiledRule>();
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                var title = string.IsNullOrWhiteSpace(rule.Title) ? "правило " + (i + 1) : rule.Title!;
                var rulePath = path + Sep + title;
                var when = Conditions(rulePath, rule.When);
                var steps = new List<CompiledStep>();
                for (var s = 0; s < rule.Steps.Count; s++)
                    steps.Add(Step(rulePath + Sep + "шаг " + (s + 1), rule.Steps[s]));

                if (rule.Allow != null)
                {
                    foreach (var value in rule.Allow.Values) CheckKey(rulePath, value, kind);
                    if (rule.Allow.Otherwise != "drop") CheckKey(rulePath, rule.Allow.Otherwise, kind);
                }

                if (rule.Default != null) CheckResult(rulePath, rule.Default);
                compiled.Add(new CompiledRule(rule, title, when, steps, Refines(rulePath, rule.Refine)));
            }

            return compiled;
        }

        private CompiledStep Step(string path, StepDef step)
        {
            CompiledDictionary? dictionary = null;
            if (step.Inherit != null)
            {
                if (step.Inherit is not ("parent" or "host")) Issue(path, $"наследовать можно от parent или host, а не «{step.Inherit}»");
                foreach (var key in step.OnlyIf)
                    if (!_types.Contains(key) && !_disciplines.Contains(key))
                        Issue(path, $"«{key}» нет ни в типах, ни в разделах");
            }
            else if (step.Dictionary != null)
            {
                dictionary = TryGet(path, () => _runtime.Dictionary(step.Dictionary));
                var sources = step.Sources.Count > 0 ? step.Sources : step.Source != null ? [step.Source] : new List<string>();
                if (sources.Count == 0) Issue(path, "не задан источник текста");
                foreach (var source in sources) CheckSource(path, source);
            }
            else if (step.Fixed != null)
            {
                CheckResult(path, step.Fixed);
            }
            else
            {
                Issue(path, "шаг ничего не делает: нет ни наследования, ни словаря, ни результата");
            }

            return new CompiledStep(step, dictionary, Refines(path, step.Refine));
        }

        private IReadOnlyList<CompiledRefine> Refines(string path, List<RefineDef> refines) =>
            refines.Select(r =>
            {
                CheckResult(path, r.To);
                return new CompiledRefine(Conditions(path, r.When), r.To);
            }).ToList();

        private IReadOnlyList<CompiledCondition> Conditions(string path, List<ConditionDef> conditions)
        {
            CheckConditions(path, conditions);
            return TryGet(path, () => _runtime.Conditions(conditions)) ?? [];
        }

        private void CheckConditions(string path, List<ConditionDef> conditions)
        {
            foreach (var condition in conditions)
            {
                if (condition.Category is { Count: 0 } || condition.Mode is { Count: 0 } || condition.Type is { Count: 0 } || condition.Gives is { Count: 0 })
                    Issue(path, "у условия пустой список — оно никогда не выполнится (а с «не» — всегда)");
                if (condition.Matches is { Length: 0 })
                    Issue(path, "у условия пустой шаблон");
                if (condition.Source != null) CheckSource(path, condition.Source);
                if (knownCategory != null)
                    foreach (var category in (condition.Category ?? []).Where(c => !knownCategory(c)))
                        Issue(path, $"категории «{category}» нет в этой версии Revit");
                if ((condition.Matches != null || condition.Dictionary != null) && condition.Source == null)
                    Issue(path, "у условия с шаблоном или словарём не задан источник текста");
                if (condition.Matches != null) Try(path, () => _runtime.Pattern(condition.Matches));
                if (condition.Dictionary != null && !profile.Dictionaries.ContainsKey(condition.Dictionary))
                    Issue(path, $"нет словаря «{condition.Dictionary}»");
                foreach (var type in condition.Type ?? [])
                    if (!_types.Contains(type))
                        Issue(path, $"нет типа «{type}»");
                foreach (var mode in condition.Mode ?? [])
                    if (profile.Modes.All(m => m.Key != mode))
                        Issue(path, $"нет режима «{mode}»");
            }
        }

        private void CheckSource(string path, string source)
        {
            if (source != ProfileRuntime.StepSourceName && !profile.Texts.ContainsKey(source))
                Issue(path, $"нет источника текста «{source}»");
        }

        private void CheckKey(string path, string key, RuleKind kind)
        {
            var known = kind == RuleKind.Discipline ? _disciplines.Contains(key) : _types.Contains(key) || _disciplines.Contains(key);
            if (!known) Issue(path, $"«{key}» нет в {(kind == RuleKind.Discipline ? "разделах" : "типах")}");
        }

        private void CheckResult(string path, ResultDef result)
        {
            if (result.Discipline != null && !_disciplines.Contains(result.Discipline)) Issue(path, $"нет раздела «{result.Discipline}»");
            if (result.Type != null && !_types.Contains(result.Type)) Issue(path, $"нет типа «{result.Type}»");
            if (result.Position != null && !_positions.Contains(result.Position)) Issue(path, $"нет позиции «{result.Position}» в классификаторе");
            if (result.Pipe != null) CheckResult(path, result.Pipe);
            if (result.Air != null) CheckResult(path, result.Air);
        }

        private CompiledTable Table(string name, TableDef table)
        {
            var path = "Таблицы" + Sep + name;
            var rows = new Dictionary<(string, string), string>();
            for (var i = 0; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                var rowPath = path + Sep + "строка " + (i + 1);
                if (row.Discipline != "*" && !_disciplines.Contains(row.Discipline)) Issue(rowPath, $"нет раздела «{row.Discipline}»");
                if (!_types.Contains(row.Type)) Issue(rowPath, $"нет типа «{row.Type}»");
                if (!_positions.Contains(row.Position)) Issue(rowPath, $"нет позиции «{row.Position}» в классификаторе");
                if (rows.ContainsKey((row.Discipline, row.Type))) Issue(rowPath, $"для «{row.Discipline}» + «{row.Type}» уже есть строка");
                rows[(row.Discipline, row.Type)] = row.Position;
            }

            foreach (var fallback in table.Fallbacks)
            foreach (var type in fallback.Value.Prepend(fallback.Key))
                if (!_types.Contains(type))
                    Issue(path + Sep + "замены", $"нет типа «{type}»");

            return new CompiledTable(table, rows);
        }

        private void Try(string path, Action action) => TryGet<object?>(path, () =>
        {
            action();
            return null;
        });

        private T? TryGet<T>(string path, Func<T> action) where T : class?
        {
            try
            {
                return action();
            }
            catch (PatternException exception)
            {
                // Битый шаблон словаря уже назван по месту — не повторяем его у каждого шага, который берёт этот словарь.
                if (!Issues.Any(i => i.Message == exception.Message)) Issue(path, exception.Message);
                return null;
            }
            catch (ArgumentException exception)
            {
                Issue(path, exception.Message);
                return null;
            }
        }
    }
}
