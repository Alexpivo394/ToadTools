using Autodesk.Revit.DB;
using ParamChecker.Core.Checking;

namespace ParamChecker.Services;

/// <summary>
///     Читает значения параметров элементов: сначала у экземпляра, затем у типа. Типы кешируются — у тысяч
///     элементов их обычно десятки.
/// </summary>
public sealed class ParameterReader(Document document)
{
    private readonly Dictionary<ElementId, ElementType?> _types = new();

    public ParamValue Read(Element element, string name)
    {
        var parameter = element.LookupParameter(name) ?? TypeOf(element)?.LookupParameter(name);
        return parameter is null ? ParamValue.Missing : Value(parameter);
    }

    public ElementType? TypeOf(Element element)
    {
        var id = element.GetTypeId();
        if (id == ElementId.InvalidElementId) return null;

        if (!_types.TryGetValue(id, out var type))
        {
            type = document.GetElement(id) as ElementType;
            _types[id] = type;
        }

        return type;
    }

    private static ParamValue Value(Parameter parameter)
    {
        switch (parameter.StorageType)
        {
            case StorageType.String:
                return new ParamValue(true, parameter.AsString());

            case StorageType.Double:
                return parameter.HasValue ? new ParamValue(true, parameter.AsValueString(), DisplayValue(parameter)) : Empty;

            case StorageType.Integer:
                if (!parameter.HasValue) return Empty;
                var integer = parameter.AsInteger();
                return new ParamValue(true, parameter.AsValueString() ?? integer.ToString(), integer);

            case StorageType.ElementId:
                return !parameter.HasValue || parameter.AsElementId() == ElementId.InvalidElementId
                    ? Empty
                    : new ParamValue(true, parameter.AsValueString());

            default:
                return new ParamValue(true, parameter.AsValueString());
        }
    }

    private static readonly ParamValue Empty = new(true, null);

    /// <summary>Число в единицах, которые видит пользователь: условие «Длина больше 1000» — это миллиметры, а не футы.</summary>
    private static double DisplayValue(Parameter parameter)
    {
        var value = parameter.AsDouble();
        try
        {
#if REVIT2021_OR_GREATER
            return UnitUtils.ConvertFromInternalUnits(value, parameter.GetUnitTypeId());
#else
            return UnitUtils.ConvertFromInternalUnits(value, parameter.DisplayUnitType);
#endif
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return value; // безразмерный параметр — значение как есть
        }
    }
}
