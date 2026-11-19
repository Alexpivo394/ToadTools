using System.IO;
using System.Text;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Running;

namespace ClassifierCode.Engine.Kit;

/// <summary>Модель → режим: явная настройка из work\modes.json, иначе по шифру в имени файла.</summary>
public static class ModeResolver
{
    public static string? Resolve(CompiledProfile profile, string modelName, IReadOnlyDictionary<string, string>? modes)
    {
        if (modes != null && modes.TryGetValue(modelName, out var key) && profile.Modes.ContainsKey(key)) return key;
        return ModeDetector.DetectMode(profile, modelName);
    }
}

/// <summary>Элементы модели, которые «Заполнить» берёт в работу (категории профиля), с движком режима.</summary>
public sealed record KitModel(ExportModel Export, string? Mode, ClassificationEngine? Engine, IReadOnlyList<IElementFacts> Elements,
    IReadOnlyDictionary<long, ExportElement> Raw);

public static class KitElements
{
    public static IEnumerable<KitModel> Of(CompiledProfile profile, IEnumerable<ExportModel> models, IReadOnlyDictionary<string, string>? modes)
    {
        var categories = new HashSet<string>(profile.Source.Categories);
        foreach (var model in models)
        {
            var mode = ModeResolver.Resolve(profile, model.Model, modes);
            var engine = mode != null ? new ClassificationEngine(profile, mode) : null;
            var facts = ExportFacts.Load(model);
            var raw = model.Elements.ToDictionary(e => e.Id);
            yield return new KitModel(model, mode, engine, facts.Where(f => categories.Contains(f.Category)).ToList(), raw);
        }
    }

    /// <summary>«Семейство / Тип / «Наименование»» — как элемент видит человек.</summary>
    public static string Label(IElementFacts facts)
    {
        var name = facts.InstanceParameter("ADSK_Наименование");
        if (string.IsNullOrWhiteSpace(name)) name = facts.TypeParameter("ADSK_Наименование");
        return $"{facts.FamilyName} / {facts.TypeName} / «{name}»";
    }
}

/// <summary>TSV для агента и Excel: табуляции и переводы строк внутри значений заменяются пробелами.</summary>
public static class TsvWriter
{
    public static void Write(string path, IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine(string.Join("\t", header));
        foreach (var row in rows) writer.WriteLine(string.Join("\t", row.Select(Cell)));
    }

    private static string Cell(object? value) =>
        (value?.ToString() ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
