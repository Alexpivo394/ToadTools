using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace ParamChecker.Services;

/// <summary>
///     Имена параметров для подсказок при вводе — без открытия проверяемых моделей. Источники: файл общих
///     параметров (ФОП), параметры и семейства открытого проекта, системные параметры Revit. Имя, которого нет
///     ни в одном источнике, скорее всего, опечатка — окна подсвечивают такие имена.
/// </summary>
public sealed class ParameterCatalog
{
    private static IReadOnlyList<string>? _builtIn;

    private readonly HashSet<string> _known = new(StringComparer.CurrentCultureIgnoreCase);

    private ParameterCatalog(IEnumerable<string> names, bool hasSharedParameterFile)
    {
        foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n))) _known.Add(name.Trim());
        Names = _known.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
        HasSharedParameterFile = hasSharedParameterFile;
    }

    public static ParameterCatalog Empty { get; } = new([], false);

    public IReadOnlyList<string> Names { get; }

    public bool Contains(string name) => _known.Contains(name.Trim());

    /// <summary>
    ///     Подключён ФОП. Без него неизвестны все ADSK-параметры —
    ///     и подсвечивать их как опечатки бессмысленно.
    /// </summary>
    public bool HasSharedParameterFile { get; }

    /// <summary>Имя похоже на опечатку: ФОП подключён, а этого имени нет ни в нём, ни в проекте, ни среди системных.</summary>
    public bool IsSuspicious(string name) => HasSharedParameterFile && name.Trim().Length > 0 && !Contains(name);

    public static ParameterCatalog Build(Application application, Document? document)
    {
        var shared = SharedParameters(application).ToList();
        List<string> project = document is null ? [] : ProjectParameters(document).ToList();
        IEnumerable<string> families = document is null ? [] : FamilyParameters(document);

        // Сверять имена есть смысл только с ФОП: параметры проекта и семейств есть и в пустом проекте из шаблона,
        // и по ним одним опечатку не отличить от ADSK-параметра, которого в шаблоне нет.
        return new ParameterCatalog(shared.Concat(project).Concat(families).Concat(_builtIn ??= BuiltInParameters()), shared.Count > 0);
    }

    private static IEnumerable<string> SharedParameters(Application application)
    {
        try
        {
            var file = application.OpenSharedParameterFile();
            if (file is null) return [];
            return file.Groups.Cast<DefinitionGroup>().SelectMany(group => group.Definitions.Cast<Definition>()).Select(d => d.Name).ToList();
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return []; // ФОП не задан или недоступен
        }
    }

    private static IEnumerable<string> ProjectParameters(Document document)
    {
        var result = new List<string>();
        var iterator = document.ParameterBindings.ForwardIterator();
        while (iterator.MoveNext())
            if (iterator.Key is Definition definition) result.Add(definition.Name);
        return result;
    }

    /// <summary>Параметры типов загруженных семейств — по одному типу на семейство, этого хватает.</summary>
    private static IEnumerable<string> FamilyParameters(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .GroupBy(symbol => symbol.FamilyName)
            .SelectMany(group => group.First().Parameters.Cast<Parameter>())
            .Select(parameter => parameter.Definition?.Name ?? "")
            .ToList();

    /// <summary>Подписи системных параметров на языке Revit. Считаются один раз за сеанс.</summary>
    private static IReadOnlyList<string> BuiltInParameters()
    {
        var result = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (BuiltInParameter parameter in Enum.GetValues(typeof(BuiltInParameter)))
        {
            if (parameter == BuiltInParameter.INVALID) continue;
            try
            {
                var label = LabelUtils.GetLabelFor(parameter);
                if (!string.IsNullOrWhiteSpace(label)) result.Add(label);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // у части значений перечисления подписи нет
            }
        }

        return result.ToList();
    }
}
