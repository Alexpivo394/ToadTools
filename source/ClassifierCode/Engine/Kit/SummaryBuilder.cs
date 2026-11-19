using System.IO;
using System.Text;
using ClassifierCode.Engine.Facts;

namespace ClassifierCode.Engine.Kit;

public sealed record SummaryGroup(string Model, string Category, string Family, string Type, string Name, int Count);
public sealed record SummarySystem(string Name, int Count, string Models);
public sealed record SummaryWorkset(string Model, string Workset, int Count);
public sealed record SummaryParameter(string Name, double FilledShare, int Count);
public sealed record SummaryValue(string Parameter, string Value, int Count);
public sealed record SummaryModel(string Model, int Elements, int Categories, int WithCode, string Binding);

/// <summary>Обзор выгрузок для агента: что за модели, как называются системы и семейства, что заполнено.</summary>
public sealed class KitSummary
{
    public List<SummaryModel> Models { get; } = [];
    public List<SummaryGroup> Groups { get; } = [];
    public List<SummarySystem> Systems { get; } = [];
    public List<SummaryWorkset> Worksets { get; } = [];
    public List<SummaryParameter> Parameters { get; } = [];
    public List<SummaryValue> Values { get; } = [];
    public List<(string Code, int Count)> CurrentCodes { get; } = [];

    public void Write(string folder)
    {
        TsvWriter.Write(Path.Combine(folder, "models.tsv"), ["модель", "элементов", "категорий", "с кодом", "заполнялась профилем"],
            Models.Select(m => new object?[] { m.Model, m.Elements, m.Categories, m.WithCode, m.Binding }));
        TsvWriter.Write(Path.Combine(folder, "groups.tsv"), ["модель", "категория", "семейство", "тип", "ADSK_Наименование", "шт"],
            Groups.Select(g => new object?[] { g.Model, g.Category, g.Family, g.Type, g.Name, g.Count }));
        TsvWriter.Write(Path.Combine(folder, "systems.tsv"), ["система (имя, тип, сокращение)", "элементов", "модели"],
            Systems.Select(s => new object?[] { s.Name, s.Count, s.Models }));
        TsvWriter.Write(Path.Combine(folder, "worksets.tsv"), ["модель", "рабочий набор", "элементов"],
            Worksets.Select(w => new object?[] { w.Model, w.Workset, w.Count }));
        TsvWriter.Write(Path.Combine(folder, "parameters.tsv"), ["параметр", "доля заполненных", "элементов"],
            Parameters.Select(p => new object?[] { p.Name, p.FilledShare.ToString("0.00"), p.Count }));
        TsvWriter.Write(Path.Combine(folder, "values.tsv"), ["параметр", "значение", "элементов"],
            Values.Select(v => new object?[] { v.Parameter, v.Value, v.Count }));
        TsvWriter.Write(Path.Combine(folder, "current-codes.tsv"), ["код в модели", "элементов"], CurrentCodes.Select(c => new object?[] { c.Code, c.Count }));
    }

    public string Text()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Модели:");
        foreach (var m in Models)
            sb.AppendLine($"  {m.Model}: элементов {m.Elements}, категорий {m.Categories}, уже с кодом {m.WithCode}{(m.Binding.Length > 0 ? ", заполнялась: " + m.Binding : string.Empty)}");
        sb.AppendLine($"Групп «семейство / тип / наименование»: {Groups.Count}; систем: {Systems.Count}; рабочих наборов: {Worksets.Select(w => w.Workset).Distinct().Count()}; параметров: {Parameters.Count}");
        sb.AppendLine("Частые системы:");
        foreach (var s in Systems.Take(30)) sb.AppendLine($"  {s.Name} — {s.Count}");
        return sb.ToString();
    }
}

public static class SummaryBuilder
{
    /// <summary>Параметры, значения которых показываются целиком: по ним обычно определяется раздел и подсистема.</summary>
    public static readonly IReadOnlyList<string> ValueParameters =
        ["ADSK_Группирование", "ADSK_Имя системы", "ADSK_Система", "ADSK_Штамп_Шифр комплекта", "ADSK_Штамп_Шифр_комплекта", "ИмяСистемы"];

    public static KitSummary Build(IEnumerable<ExportModel> models)
    {
        var summary = new KitSummary();
        var groups = new Dictionary<(string, string, string, string, string), int>();
        var systems = new Dictionary<string, (int Count, HashSet<string> Models)>();
        var worksets = new Dictionary<(string, string), int>();
        var parameters = new Dictionary<string, int>();
        var values = new Dictionary<(string, string), int>();
        var codes = new Dictionary<string, int>();
        var total = 0;

        foreach (var model in models)
        {
            var facts = ExportFacts.Load(model);
            var categories = new HashSet<string>();
            var withCode = 0;
            foreach (ExportFacts f in facts)
            {
                total++;
                categories.Add(f.Category);
                var name = f.InstanceParameter("ADSK_Наименование");
                if (string.IsNullOrWhiteSpace(name)) name = f.TypeParameter("ADSK_Наименование");
                Count(groups, (model.Model, f.Category, f.FamilyName, f.TypeName, name));

                foreach (var token in f.SystemText.Split('|').Select(t => t.Trim()).Where(t => t.Length > 0).Distinct())
                {
                    systems.TryGetValue(token, out var s);
                    s.Models ??= [];
                    s.Models.Add(model.Model);
                    systems[token] = (s.Count + 1, s.Models);
                }

                if (!string.IsNullOrEmpty(f.Workset)) Count(worksets, (model.Model, f.Workset!));

                var seen = new HashSet<string>();
                foreach (var p in f.Element.Parameters.Where(p => p.Value.Length > 0)) seen.Add(p.Name);
                if (f.Element.TypeId != null && model.Types.TryGetValue(f.Element.TypeId, out var type))
                    foreach (var p in type.Parameters.Where(p => p.Value.Length > 0)) seen.Add(p.Name);
                foreach (var p in seen) Count(parameters, p);

                foreach (var p in ValueParameters)
                {
                    var value = f.InstanceParameter(p);
                    if (string.IsNullOrWhiteSpace(value)) value = f.TypeParameter(p);
                    if (!string.IsNullOrWhiteSpace(value)) Count(values, (p, value));
                }

                if (!string.IsNullOrEmpty(f.Element.CurrentCode))
                {
                    withCode++;
                    Count(codes, f.Element.CurrentCode!);
                }
            }

            var binding = model.Binding != null ? $"{model.Binding.Name} {model.Binding.Version} / {model.Binding.Mode}" : string.Empty;
            summary.Models.Add(new SummaryModel(model.Model, model.Elements.Count, categories.Count, withCode, binding));
        }

        summary.Groups.AddRange(groups.OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2).ThenByDescending(g => g.Value)
            .Select(g => new SummaryGroup(g.Key.Item1, g.Key.Item2, g.Key.Item3, g.Key.Item4, g.Key.Item5, g.Value)));
        summary.Systems.AddRange(systems.OrderByDescending(s => s.Value.Count)
            .Select(s => new SummarySystem(s.Key, s.Value.Count, string.Join(", ", s.Value.Models.OrderBy(m => m)))));
        summary.Worksets.AddRange(worksets.OrderBy(w => w.Key.Item1).ThenByDescending(w => w.Value)
            .Select(w => new SummaryWorkset(w.Key.Item1, w.Key.Item2, w.Value)));
        summary.Parameters.AddRange(parameters.OrderByDescending(p => p.Value)
            .Select(p => new SummaryParameter(p.Key, total == 0 ? 0 : Math.Round((double)p.Value / total, 2), p.Value)));
        summary.Values.AddRange(values.OrderBy(v => v.Key.Item1).ThenByDescending(v => v.Value).Select(v => new SummaryValue(v.Key.Item1, v.Key.Item2, v.Value)));
        summary.CurrentCodes.AddRange(codes.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => (c.Key, c.Value)));
        return summary;
    }

    private static void Count<T>(Dictionary<T, int> counts, T key) where T : notnull
    {
        counts.TryGetValue(key, out var n);
        counts[key] = n + 1;
    }
}
