namespace ClassifierCode.Models;

/// <summary>Итоги прогона.</summary>
public sealed class ClassifierReport(SectionOption section, int total)
{
    public SectionOption Section { get; } = section;

    /// <summary>Сколько элементов попало в обработку.</summary>
    public int Total { get; } = total;

    public int WrittenInstances { get; set; }
    public int WrittenTypes { get; set; }
    public int KeptExisting { get; set; }
    public int Busy { get; set; }

    public Dictionary<string, int> Skipped { get; } = new();
    public Dictionary<string, int> ByCode { get; } = new();
    public List<KeyValuePair<ElementId, string>> Unclassified { get; } = [];
    public List<KeyValuePair<ElementId, string>> MissingParameters { get; } = [];
    public List<KeyValuePair<ElementId, string>> Failed { get; } = [];
    public List<string> TypeConflicts { get; } = [];
    public List<string> Notes { get; } = [];

    /// <summary>Элементы, которые не удалось заполнить: не классифицированы, без параметров, ошибки записи.</summary>
    public List<ElementId> ProblemIds =>
        Unclassified.Select(x => x.Key)
            .Concat(MissingParameters.Select(x => x.Key))
            .Concat(Failed.Select(x => x.Key))
            .ToList();

    public void AddFailed(Element element)
    {
        var where = element.GroupId != ElementId.InvalidElementId ? " (в группе)" : string.Empty;
        Failed.Add(new KeyValuePair<ElementId, string>(element.Id, Describe(element) + where));
    }

    public void AddSkipped(string reason)
    {
        Skipped.TryGetValue(reason, out var current);
        Skipped[reason] = current + 1;
    }

    public void AddUnclassified(Element element, string? reason)
    {
        Unclassified.Add(new KeyValuePair<ElementId, string>(element.Id, Describe(element) + ": " + reason));
    }

    public void AddMissingParameters(Element element)
    {
        MissingParameters.Add(new KeyValuePair<ElementId, string>(element.Id, Describe(element)));
    }

    public void CountCode(ClassifierItem item, int count)
    {
        var key = item.Code + " " + item.Description;
        ByCode.TryGetValue(key, out var current);
        ByCode[key] = current + count;
    }

    public static string Describe(Element element)
    {
        var category = element.Category != null ? element.Category.Name : "<без категории>";
        var family = element.Document.GetElement(element.GetTypeId()) is ElementType type ? type.FamilyName : element.Name;
        return category + " / " + family;
    }
}
