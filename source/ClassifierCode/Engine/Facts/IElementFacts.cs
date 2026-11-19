namespace ClassifierCode.Engine.Facts;

/// <summary>К какой сети относится элемент: по категории, а для оборудования — по подключённым системам.</summary>
public enum NetworkDomain
{
    Pipe,
    Air,
    Other
}

/// <summary>
///     Всё, что движок знает об элементе, без типов Revit. Строки параметров — как в старом GetString:
///     текст для строковых, AsValueString для остальных, "" если параметра нет.
/// </summary>
public interface IElementFacts
{
    long Id { get; }

    /// <summary>Имя BuiltInCategory ("OST_PipeCurves"), "" если категории нет.</summary>
    string Category { get; }

    NetworkDomain Domain { get; }
    bool HasType { get; }
    string InstanceName { get; }

    /// <summary>"" если нет типа.</summary>
    string FamilyName { get; }

    string TypeName { get; }
    string InstanceParameter(string name);
    string TypeParameter(string name);

    /// <summary>Непустые значения строковых параметров экземпляра в порядке ParameterSet.</summary>
    IEnumerable<string> InstanceStringValues();

    IEnumerable<string> TypeStringValues();
    string InstanceComments { get; }
    string TypeComments { get; }

    /// <summary>Имена и сокращения систем элемента, их типы и классификация — без нормализации.</summary>
    string SystemText { get; }

    /// <summary>null — модель не совместная.</summary>
    string? Workset { get; }

    string DocumentTitle { get; }

    /// <summary>Родительское семейство (SuperComponent).</summary>
    IElementFacts? Parent { get; }

    /// <summary>Изолируемый элемент — для изоляции.</summary>
    IElementFacts? Host { get; }
}
