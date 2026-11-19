using ClassifierCode.Editor.ViewModels;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Services;

/// <summary>
///     Модель Revit → выгрузка для ИИ. Значения берутся через <see cref="FactsBuilder" /> — теми же методами,
///     что и при «Заполнить», поэтому движок на выгрузке и в Revit видит одно и то же.
/// </summary>
public sealed class FactsExporter(Document doc, IReadOnlyList<OutputField> outputs)
{
    private readonly FactsBuilder _facts = new(doc);
    private readonly OutputField? _code = outputs.FirstOrDefault(o => o.Field == "code");
    private readonly OutputField? _description = outputs.FirstOrDefault(o => o.Field == "description" && !string.IsNullOrWhiteSpace(o.Parameter));

    /// <summary>Категории выгрузки — весь каталог редактора: агент может завести в профиле любую из них.</summary>
    public static IReadOnlyList<string> CategoryNames => CategoryCatalog.All.Select(c => c.Name).ToList();

    /// <param name="modelName">Имя модели, как у неё в работе. У открытой отсоединённой копии Document.Title другой (с суффиксом отсоединения), а раздел и document.title считаются по настоящему имени.</param>
    public ExportModel Export(string source, string? modelName = null)
    {
        var categories = ElementCollector.Categories(CategoryNames);
        var model = new ExportModel
        {
            Model = string.IsNullOrWhiteSpace(modelName) ? doc.Title : modelName!,
            Source = source,
            RevitVersion = doc.Application.VersionNumber,
            ExportedAt = DateTime.Now,
            Workshared = doc.IsWorkshared,
            Categories = categories.Select(c => c.ToString()).ToList(),
            Outputs = outputs.ToList(),
            Binding = ProfileBinding.Read(doc)
        };

        var elements = new FilteredElementCollector(doc)
            .WherePasses(new ElementMulticategoryFilter(categories.ToList()))
            .WhereElementIsNotElementType()
            .Where(e => !e.ViewSpecific)
            .ToList();

        // Родители и изолируемые элементы нужны движку, даже если их категории нет в каталоге.
        var queue = new Queue<Element>(elements);
        var seen = new HashSet<ElementId>(elements.Select(e => e.Id));
        while (queue.Count > 0)
        {
            var element = queue.Dequeue();
            AddElement(model, element);
            foreach (var related in Related(element))
                if (seen.Add(related.Id))
                    queue.Enqueue(related);
        }

        return model;
    }

    private IEnumerable<Element> Related(Element element)
    {
        if (element is FamilyInstance { SuperComponent: { } parent }) yield return parent;
        if (element is InsulationLiningBase insulation && doc.GetElement(insulation.HostElementId) is { } host) yield return host;
    }

    private void AddElement(ExportModel model, Element element)
    {
        var facts = _facts.Get(element);
        var type = doc.GetElement(element.GetTypeId());
        string? typeId = null;
        if (type is ElementType elementType)
        {
            typeId = RevitCompat.IdValue(type.Id).ToString();
            if (!model.Types.ContainsKey(typeId))
                model.Types[typeId] = new ExportType
                {
                    Family = elementType.FamilyName ?? string.Empty,
                    Name = elementType.Name ?? string.Empty,
                    Comments = facts.TypeComments,
                    Parameters = Parameters(type)
                };
        }

        var code = _code == null ? null : OutputParameters.Find(element, _code) ?? (type != null ? OutputParameters.Find(type, _code) : null);
        var description = _description == null ? null : OutputParameters.Find(element, _description) ?? (type != null ? OutputParameters.Find(type, _description) : null);
        model.Elements.Add(new ExportElement
        {
            Id = facts.Id,
            Category = facts.Category,
            TypeId = typeId,
            Name = facts.InstanceName,
            Comments = facts.InstanceComments,
            SystemText = facts.SystemText,
            Domain = ExportFacts.DomainKey(facts.Domain),
            Workset = facts.Workset,
            ParentId = facts.Parent?.Id,
            HostId = facts.Host?.Id,
            HasCodeParameter = code != null,
            CurrentCode = NullIfEmpty(code?.AsString()),
            CurrentDescription = NullIfEmpty(description?.AsString()),
            Parameters = Parameters(element)
        });
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Параметры в порядке ParameterSet; пустые — только если имя повторяется (LookupParameter берёт первый).</summary>
    private static List<ExportParameter> Parameters(Element element)
    {
        var all = new List<(Parameter Source, ExportParameter Export)>();
        foreach (Parameter parameter in element.Parameters)
        {
            var name = parameter.Definition?.Name;
            if (string.IsNullOrEmpty(name)) continue;
            all.Add((parameter, new ExportParameter { Name = name!, Value = FactsBuilder.GetString(parameter), IsString = parameter.StorageType == StorageType.String }));
        }

        var repeated = new HashSet<string>(all.GroupBy(p => p.Export.Name).Where(g => g.Count() > 1).Select(g => g.Key));
        foreach (var name in repeated)
        {
            // Порядок ParameterSet не трогаем (по нему идёт firstStringMatching), а того, кого вернёт LookupParameter, помечаем:
            // по имени FactsBuilder читает именно его.
            var lookup = element.LookupParameter(name);
            var first = all.FindIndex(p => p.Export.Name == name);
            var chosen = lookup == null ? -1 : all.FindIndex(p => p.Export.Name == name && p.Source.Id == lookup.Id);
            if (chosen > first) all[chosen].Export.Lookup = true;
        }

        return all.Select(p => p.Export).Where(p => p.Value.Length > 0 || repeated.Contains(p.Name)).ToList();
    }
}
