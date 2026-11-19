using System.IO;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Running;

namespace ClassifierCode.Engine.Kit;

/// <summary>Похоже на ошибку в модели: элемент выбивается из группы таких же.</summary>
public sealed record Anomaly(string Model, long Id, string Label, string Where, string Expected, string Why);

/// <summary>
///     Ищет ошибки проектировщиков, под которые нельзя подгонять профиль: в группе одинаковых элементов
///     («семейство / тип / наименование») редкое меньшинство получило другой раздел или лежит в другом рабочем наборе.
///     Профиль решает по большинству, а эти элементы правятся в модели.
/// </summary>
public static class AnomalyFinder
{
    private const int MinGroup = 5;

    public static IReadOnlyList<Anomaly> Find(CompiledProfile profile, IEnumerable<ExportModel> models, IReadOnlyDictionary<string, string>? modes,
        double share = 0.05, int max = 10)
    {
        var found = new List<Anomaly>();
        foreach (var model in KitElements.Of(profile, models, modes))
        {
            if (model.Engine == null) continue;
            var rows = model.Elements.Select(f => (Facts: f, Label: KitElements.Label(f), Result: model.Engine.Classify(f))).ToList();
            foreach (var group in rows.GroupBy(r => (r.Facts.Category, r.Label)))
            {
                var items = group.ToList();
                if (items.Count < MinGroup) continue;

                // Раздел и у не классифицированных: «раздел B + тип» без строки таблицы — тот же выброс, что и другой код.
                Minority(items, r => r.Result.Discipline,
                    key => key == null ? "—" : profile.DisciplineTitle(key), "раздел", share, max,
                    (r, where, expected) => found.Add(new Anomaly(model.Export.Model, r.Facts.Id, r.Label, where, expected,
                        $"раздел не как у остальных таких же элементов (система: {Cut(r.Facts.SystemText)})")));

                Minority(items, r => r.Facts.Workset, key => key ?? "—", "рабочий набор", share, max,
                    (r, where, expected) => found.Add(new Anomaly(model.Export.Model, r.Facts.Id, r.Label, where, expected,
                        "рабочий набор не как у остальных таких же элементов")));
            }
        }

        return found;
    }

    private static void Minority<T>(List<T> items, Func<T, string?> key, Func<string?, string> title, string what, double share, int max,
        Action<T, string, string> report)
    {
        var keyed = items.Where(i => key(i) != null).ToList();
        if (keyed.Count < MinGroup) return;
        var counts = keyed.GroupBy(key).OrderByDescending(g => g.Count()).ToList();
        if (counts.Count < 2) return;
        var majority = counts[0];
        if (majority.Count() < keyed.Count * (1 - share)) return; // нет явного большинства — это не ошибка, а разные элементы
        foreach (var minor in counts.Skip(1))
        {
            if (minor.Count() > max || minor.Count() > keyed.Count * share) continue;
            foreach (var item in minor) report(item, $"{what}: {title(minor.Key)}", $"{what}: {title(majority.Key)}");
        }
    }

    private static string Cut(string text)
    {
        text = text.Trim(' ', '|');
        return text.Length <= 60 ? text : text.Substring(0, 60) + "…";
    }

    public static void Write(string path, IReadOnlyList<Anomaly> anomalies) =>
        TsvWriter.Write(path, ["модель", "Id", "элемент", "сейчас", "у остальных", "почему"],
            anomalies.Select(a => new object?[] { a.Model, a.Id, a.Label, a.Where, a.Expected, a.Why }));
}

/// <summary>Стоящий в модели код расходится с тем, что дал бы профиль.</summary>
public sealed record CodeDiff(string Model, long Id, string Label, string Current, string Profile, string? FilledByWouldGive, bool IsManual);

public static class DiffFinder
{
    /// <param name="filledBy">Профиль, которым модель заполнялась. Ручная правка — где стоящий код не совпадает с его результатом.</param>
    public static IReadOnlyList<CodeDiff> Find(CompiledProfile current, CompiledProfile? filledBy, IEnumerable<ExportModel> models,
        IReadOnlyDictionary<string, string>? modes)
    {
        var diffs = new List<CodeDiff>();
        foreach (var model in KitElements.Of(current, models, modes))
        {
            if (model.Engine == null) continue;
            // Ручную правку видно, только если модель заполнялась именно этим профилем и этой его версией.
            ClassificationEngine? old = null;
            if (filledBy != null && model.Export.Binding is { } binding && binding.Id == filledBy.Source.Id &&
                binding.Version == filledBy.Source.Version && filledBy.Modes.ContainsKey(binding.Mode))
                old = new ClassificationEngine(filledBy, binding.Mode);

            foreach (var facts in model.Elements)
            {
                var stored = model.Raw[facts.Id].CurrentCode;
                if (string.IsNullOrEmpty(stored)) continue;
                var now = model.Engine.Classify(facts).Position?.Code ?? string.Empty;
                if (now == stored) continue;
                // Профиль заполнения не дал кода (before пустой), а код стоит — тоже поставили руками.
                var before = old == null ? null : old.Classify(facts).Position?.Code ?? string.Empty;
                diffs.Add(new CodeDiff(model.Export.Model, facts.Id, KitElements.Label(facts), stored!, now, before, before != null && before != stored));
            }
        }

        return diffs;
    }

    public static void Write(string path, IReadOnlyList<CodeDiff> diffs) =>
        TsvWriter.Write(path, ["модель", "Id", "элемент", "в модели", "по профилю", "дал бы профиль заполнения", "правка руками"],
            diffs.Select(d => new object?[] { d.Model, d.Id, d.Label, d.Current, d.Profile, d.FilledByWouldGive, d.IsManual ? "да" : "" }));
}
