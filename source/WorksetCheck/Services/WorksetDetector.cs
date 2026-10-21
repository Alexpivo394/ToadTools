using System.Text.RegularExpressions;
using WorksetCheck.Models;

namespace WorksetCheck.Services;

/// <summary>Что удалось определить по модели; null — не нашлось.</summary>
public sealed record DetectedWorksets(string? CadWorkset, string? GridsWorkset, string? OpeningsWorkset);

/// <summary>
///     Угадывает имена РН по смыслу: где лежат оси, уровни и связи, и как называются наборы.
///     Результат только подставляется в поля — пользователь его проверяет и правит.
/// </summary>
public sealed class WorksetDetector(Document document)
{
    private static readonly Regex GridsName = new(@"ос[иья]\b|уровн|сетк|grid|level", RegexOptions.IgnoreCase);
    private static readonly Regex OpeningsName = new(@"отверст|проём|проем|\bзно\b|задани|opening|hole", RegexOptions.IgnoreCase);
    private static readonly Regex CadName = new(@"dwg|cad|подлож", RegexOptions.IgnoreCase);
    private static readonly Regex LinksName = new(@"связ|link", RegexOptions.IgnoreCase);

    private readonly Dictionary<int, string> _names = new FilteredWorksetCollector(document)
        .OfKind(WorksetKind.UserWorkset)
        .ToWorksets()
        .ToDictionary(workset => workset.Id.IntegerValue, workset => workset.Name);

    public DetectedWorksets Detect()
    {
        var grids = DetectGrids();
        var openings = DetectOpenings(grids);
        return new DetectedWorksets(DetectCad(grids, openings), grids, openings);
    }

    /// <summary>РН, где больше всего осей и уровней, если его имя про оси/уровни; иначе — просто по имени.</summary>
    private string? DetectGrids()
    {
        var elements = new FilteredElementCollector(document)
            .WherePasses(new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_Grids, BuiltInCategory.OST_Levels }))
            .WhereElementIsNotElementType();

        return ByPopularity(elements).FirstOrDefault(name => GridsName.IsMatch(name))
               ?? _names.Values.FirstOrDefault(name => GridsName.IsMatch(name));
    }

    private string? DetectOpenings(string? grids)
    {
        var genericModels = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_GenericModel)
            .WhereElementIsNotElementType();

        return ByPopularity(genericModels).Concat(_names.Values)
            .FirstOrDefault(name => name != grids && OpeningsName.IsMatch(name));
    }

    /// <summary>
    ///     РН, где больше всего CAD-подложек, если его имя про DWG или связи; иначе — по имени, сначала с «DWG».
    ///     РН «#Связь имя_файла» — для связей Revit, их не предлагаем.
    /// </summary>
    private string? DetectCad(string? grids, string? openings)
    {
        var imports = new FilteredElementCollector(document)
            .OfClass(typeof(ImportInstance))
            .WhereElementIsNotElementType()
            .Where(element => !element.ViewSpecific);

        bool Candidate(string name) => name != grids && name != openings && !WorksetRules.IsLinkFileWorkset(name);

        return ByPopularity(imports).FirstOrDefault(name => Candidate(name) && (CadName.IsMatch(name) || LinksName.IsMatch(name)))
               ?? _names.Values.FirstOrDefault(name => Candidate(name) && CadName.IsMatch(name));
    }

    /// <summary>Имена РН, отсортированные по числу элементов в них.</summary>
    private IEnumerable<string> ByPopularity(IEnumerable<Element> elements)
    {
        return elements
            .GroupBy(element => element.WorksetId.IntegerValue)
            .OrderByDescending(group => group.Count())
            .Select(group => _names.TryGetValue(group.Key, out var name) ? name : null)
            .OfType<string>();
    }
}
