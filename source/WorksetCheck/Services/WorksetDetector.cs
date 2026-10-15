using System.Text.RegularExpressions;
using WorksetCheck.Models;

namespace WorksetCheck.Services;

/// <summary>Что удалось определить по модели; null — не нашлось.</summary>
public sealed record DetectedWorksets(string? LinksPrefix, string? GridsWorkset, string? OpeningsWorkset);

/// <summary>
///     Угадывает имена РН по смыслу: где лежат оси, уровни и связи, и как называются наборы.
///     Результат только подставляется в поля — пользователь его проверяет и правит.
/// </summary>
public sealed class WorksetDetector(Document document)
{
    private static readonly Regex GridsName = new(@"ос[иья]\b|уровн|сетк|grid|level", RegexOptions.IgnoreCase);
    private static readonly Regex OpeningsName = new(@"отверст|проём|проем|\bзно\b|задани|opening|hole", RegexOptions.IgnoreCase);
    private static readonly Regex LinksName = new(@"связ|link|rvt|dwg", RegexOptions.IgnoreCase);
    private static readonly char[] Separators = [' ', '_', '-', '.', ':', '(', ')'];

    private readonly Dictionary<int, string> _names = new FilteredWorksetCollector(document)
        .OfKind(WorksetKind.UserWorkset)
        .ToWorksets()
        .ToDictionary(workset => workset.Id.IntegerValue, workset => workset.Name);

    public DetectedWorksets Detect()
    {
        var grids = DetectGrids();
        var openings = DetectOpenings(grids);
        var prefix = DetectLinksPrefix(grids, openings);
        return new DetectedWorksets(prefix, grids, openings);
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
    ///     Префикс — общее начало имён РН, где лежат связи, до слова-раздела: «#Связи_АР», «#Связи_КР» → «#Связи».
    ///     Наборы, не похожие на РН связей (например, «Рабочий набор1» со случайной связью), не учитываются.
    /// </summary>
    private string? DetectLinksPrefix(string? grids, string? openings)
    {
        var links = new FilteredElementCollector(document)
            .WherePasses(new LogicalOrFilter(new ElementClassFilter(typeof(RevitLinkInstance)),
                new ElementClassFilter(typeof(ImportInstance))))
            .WhereElementIsNotElementType()
            .Where(element => !element.ViewSpecific);

        var withLinks = ByPopularity(links).Where(name => name != grids && name != openings).ToList();
        return MostCommonBase(withLinks)
               ?? MostCommonBase(_names.Values.Where(name => name != grids && name != openings && LinksName.IsMatch(name)));
    }

    private static string? MostCommonBase(IEnumerable<string> names)
    {
        return names
            .Select(BaseOf)
            .Where(prefix => prefix.Length > 0 && (LinksName.IsMatch(prefix) || !char.IsLetter(prefix[0])))
            .GroupBy(prefix => prefix, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Length)
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    /// <summary>Имя без раздела и того, что после него: «#Связи_АР_корпус1» → «#Связи».</summary>
    private static string BaseOf(string name)
    {
        var index = Sections.FirstMarkerIndex(name);
        var prefix = index >= 0 ? name.Substring(0, index) : name;
        var trimmed = prefix.TrimEnd(Separators);
        return trimmed.Length > 0 ? trimmed : prefix;
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
