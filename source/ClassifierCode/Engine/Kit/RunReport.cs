using System.IO;
using System.Text;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Running;

namespace ClassifierCode.Engine.Kit;

public sealed class ModelRun
{
    public string Model { get; init; } = string.Empty;
    public string? Mode { get; init; }
    public string? ModeTitle { get; init; }
    public string? Note { get; set; }
    public int Total { get; set; }
    public int Classified { get; set; }
    public int Skipped { get; set; }
    public int Unclassified { get; set; }

    /// <summary>Элементов, у которых нет параметра кода: получат код, когда «Заполнить» добавит параметры из ФОП.</summary>
    public int WithoutCodeParameter { get; set; }
}

public sealed record NameCount(string Label, int Count);

public sealed class CodeStat
{
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Count { get; set; }
    public List<NameCount> Top { get; set; } = [];
    internal Dictionary<string, int> Labels { get; } = new();
}

public sealed class UnclassifiedGroup
{
    public string Reason { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public int Count { get; set; }
    public List<string> SampleIds { get; } = [];
}

/// <summary>Прогон профиля по выгрузкам — как «Заполнить» без записи.</summary>
public sealed class RunResult
{
    public List<ModelRun> Models { get; } = [];
    public List<CodeStat> Codes { get; } = [];
    public List<UnclassifiedGroup> Unclassified { get; } = [];
    public Dictionary<string, int> Skipped { get; } = new();

    public string Text()
    {
        var sb = new StringBuilder();
        foreach (var m in Models)
        {
            if (m.Mode == null)
            {
                sb.AppendLine($"{m.Model}: раздел не определён — {m.Note}");
                continue;
            }

            sb.AppendLine($"{m.Model} [{m.ModeTitle}]: элементов {m.Total}, с кодом {m.Classified}, пропущено {m.Skipped}, не классифицировано {m.Unclassified}" +
                          (m.WithoutCodeParameter > 0 ? $", без параметра кода {m.WithoutCodeParameter}" : string.Empty));
        }

        var total = Models.Sum(m => m.Total);
        var done = Models.Sum(m => m.Classified);
        sb.AppendLine($"Итого: {done} из {total} получат код ({(total == 0 ? 0 : 100.0 * done / total):0.0} %), кодов {Codes.Count}, групп без кода {Unclassified.Count}.");
        sb.AppendLine("Крупнейшие группы без кода:");
        foreach (var g in Unclassified.OrderByDescending(g => g.Count).Take(15)) sb.AppendLine($"  {g.Count} — {g.Label} [{g.Mode}]: {g.Reason}");
        sb.AppendLine("Коды (первые 40):");
        foreach (var c in Codes.Take(40)) sb.AppendLine($"  {c.Code} {c.Description} — {c.Count}: {string.Join("; ", c.Top.Take(3).Select(t => $"{t.Label} ×{t.Count}"))}");
        return sb.ToString();
    }

    public void WriteMarkdown(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Прогон профиля");
        sb.AppendLine();
        sb.AppendLine("| Модель | Раздел | Элементов | С кодом | Пропущено | Без кода | Без параметра |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|");
        foreach (var m in Models)
            sb.AppendLine($"| {m.Model} | {m.ModeTitle ?? "не определён"} | {m.Total} | {m.Classified} | {m.Skipped} | {m.Unclassified} | {m.WithoutCodeParameter} |");
        sb.AppendLine();
        sb.AppendLine("## По кодам");
        foreach (var c in Codes)
            sb.AppendLine($"- **{c.Code}** {c.Description} — {c.Count}: {string.Join(" · ", c.Top.Select(t => $"{t.Label} ×{t.Count}"))}");
        sb.AppendLine();
        sb.AppendLine("## Не классифицировано");
        foreach (var g in Unclassified.OrderByDescending(g => g.Count))
            sb.AppendLine($"- {g.Count} — {g.Label} [{g.Mode}]: {g.Reason} (например, {string.Join(", ", g.SampleIds)})");
        if (Skipped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Пропущено");
            foreach (var s in Skipped.OrderByDescending(s => s.Value)) sb.AppendLine($"- {s.Value} — {s.Key}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    public void WriteTsv(string folder)
    {
        TsvWriter.Write(Path.Combine(folder, "codes.tsv"), ["код", "описание", "элементов", "частые наименования"],
            Codes.Select(c => new object?[] { c.Code, c.Description, c.Count, string.Join(" · ", c.Top.Select(t => $"{t.Label} ×{t.Count}")) }));
        TsvWriter.Write(Path.Combine(folder, "unclassified.tsv"), ["раздел", "причина", "элемент", "шт", "пример Id"],
            Unclassified.OrderByDescending(g => g.Count).Select(g => new object?[] { g.Mode, g.Reason, g.Label, g.Count, string.Join(", ", g.SampleIds) }));
    }
}

public static class RunReport
{
    private const int TopNames = 5;
    private const int Samples = 3;

    public static RunResult Build(CompiledProfile profile, IEnumerable<ExportModel> models, IReadOnlyDictionary<string, string>? modes)
    {
        var result = new RunResult();
        var codes = new Dictionary<string, CodeStat>();
        var unclassified = new Dictionary<(string, string, string), UnclassifiedGroup>();

        foreach (var model in KitElements.Of(profile, models, modes))
        {
            if (model.Engine == null)
            {
                result.Models.Add(new ModelRun
                {
                    Model = model.Export.Model, Total = model.Elements.Count,
                    Note = "по имени файла не нашлось шифра раздела; задайте раздел в work\\modes.json или шифры в профиле (modes[].fileTokens)"
                });
                continue;
            }

            var title = profile.Modes[model.Mode!].Source.Title;
            var run = new ModelRun { Model = model.Export.Model, Mode = model.Mode, ModeTitle = title };
            foreach (var facts in model.Elements)
            {
                run.Total++;
                if (!model.Raw[facts.Id].HasCodeParameter) run.WithoutCodeParameter++;
                var r = model.Engine.Classify(facts);
                var label = KitElements.Label(facts);
                switch (r.Status)
                {
                    case EngineStatus.Classified:
                        run.Classified++;
                        if (!codes.TryGetValue(r.Position!.Code, out var stat))
                            codes[r.Position.Code] = stat = new CodeStat { Code = r.Position.Code, Description = r.Position.Description };
                        stat.Count++;
                        stat.Labels.TryGetValue(label, out var n);
                        stat.Labels[label] = n + 1;
                        break;
                    case EngineStatus.Skipped:
                        run.Skipped++;
                        result.Skipped.TryGetValue(r.Reason ?? string.Empty, out var s);
                        result.Skipped[r.Reason ?? string.Empty] = s + 1;
                        break;
                    default:
                        run.Unclassified++;
                        var key = (title, r.Reason ?? string.Empty, label);
                        if (!unclassified.TryGetValue(key, out var group))
                            unclassified[key] = group = new UnclassifiedGroup { Mode = title, Reason = r.Reason ?? string.Empty, Label = label };
                        group.Count++;
                        if (group.SampleIds.Count < Samples) group.SampleIds.Add(model.Export.Model + ":" + facts.Id);
                        break;
                }
            }

            result.Models.Add(run);
        }

        foreach (var stat in codes.Values)
            stat.Top = stat.Labels.OrderByDescending(l => l.Value).Take(TopNames).Select(l => new NameCount(l.Key, l.Value)).ToList();
        result.Codes.AddRange(codes.Values.OrderBy(c => c.Code, StringComparer.Ordinal));
        result.Unclassified.AddRange(unclassified.Values);
        return result;
    }
}
