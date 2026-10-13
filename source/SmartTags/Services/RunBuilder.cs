using Autodesk.Revit.DB.Mechanical;

namespace SmartTags.Services;

/// <summary>
///     Склеивает трубы/воздуховоды/лотки в прямые участки: соседние куски одного размера и системы,
///     идущие по одной прямой (соединены напрямую или через муфту). Участок получает одну марку.
/// </summary>
internal sealed class RunBuilder(Action<string>? log)
{
    // Куски считаются соосными, если угол между ними меньше ~10°.
    private const double CollinearCos = 0.985;

    private readonly Dictionary<ElementId, MEPCurve> _curves = new();
    private readonly Dictionary<ElementId, string> _keys = new();
    private readonly Dictionary<ElementId, ElementId> _parent = new();

    public List<List<MEPCurve>> Build(IEnumerable<MEPCurve> curves)
    {
        foreach (var curve in curves)
        {
            _curves[curve.Id] = curve;
            _keys[curve.Id] = RunKey(curve);
            _parent[curve.Id] = curve.Id;
        }

        foreach (var curve in _curves.Values)
        {
            try
            {
                LinkNeighbors(curve);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Участки: элемент {RevitCompat.IdValue(curve.Id)} — {ex.Message}");
            }
        }

        return _curves.Values
            .GroupBy(curve => Find(curve.Id))
            .Select(group => group.ToList())
            .ToList();
    }

    private void LinkNeighbors(MEPCurve curve)
    {
        foreach (var neighbor in ConnectedThroughUnions(curve))
        {
            if (_curves.ContainsKey(neighbor.Id) && CanJoin(curve, neighbor))
                Union(curve.Id, neighbor.Id);
        }
    }

    /// <summary>Соседи по концевым соединителям: напрямую или через муфту.</summary>
    private static IEnumerable<MEPCurve> ConnectedThroughUnions(MEPCurve curve)
    {
        foreach (var connector in EndConnectors(curve.ConnectorManager))
        foreach (Connector linked in connector.AllRefs)
        {
            var owner = linked.Owner;
            if (owner is null || owner.Id == curve.Id) continue;

            if (owner is MEPCurve neighbor)
            {
                yield return neighbor;
                continue;
            }

            if (owner is not FamilyInstance { MEPModel: MechanicalFitting { PartType: PartType.Union } fitting })
                continue;

            foreach (var other in EndConnectors(fitting.ConnectorManager))
            {
                if (other.Id == linked.Id) continue;

                foreach (Connector far in other.AllRefs)
                {
                    if (far.Owner is MEPCurve next && next.Id != curve.Id)
                        yield return next;
                }
            }
        }
    }

    public static IEnumerable<Connector> EndConnectors(ConnectorManager? manager)
    {
        if (manager is null) yield break;

        foreach (Connector connector in manager.Connectors)
        {
            if (connector.ConnectorType == ConnectorType.End)
                yield return connector;
        }
    }

    private bool CanJoin(MEPCurve a, MEPCurve b)
    {
        if (_keys[a.Id] != _keys[b.Id]) return false;

        var da = Direction(a);
        var db = Direction(b);
        return da is not null && db is not null && Math.Abs(da.DotProduct(db)) > CollinearCos;
    }

    public static XYZ? Direction(MEPCurve curve)
    {
        if (curve.Location is not LocationCurve { Curve: { } line }) return null;

        var vector = line.GetEndPoint(1) - line.GetEndPoint(0);
        return vector.GetLength() < 1e-9 ? null : vector.Normalize();
    }

    /// <summary>Ключ участка: категория, размер, система.</summary>
    private static string RunKey(MEPCurve curve)
    {
        var category = curve.Category is { } c ? RevitCompat.IdValue(c.Id).ToString() : "?";
        return category + "|" + SizeKey(curve) + "|" + ParameterText(curve, BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
    }

    public static string SizeKey(MEPCurve curve)
    {
        var size = ParameterText(curve, BuiltInParameter.RBS_CALCULATED_SIZE);
        if (!string.IsNullOrEmpty(size)) return size;

        try
        {
            return "D" + Math.Round(curve.Diameter * 304.8, 1);
        }
        catch
        {
            // Прямоугольное сечение.
        }

        try
        {
            return "W" + Math.Round(curve.Width * 304.8, 1) + "x" + Math.Round(curve.Height * 304.8, 1);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ParameterText(Element element, BuiltInParameter id)
    {
        var parameter = element.get_Parameter(id);
        if (parameter is null) return string.Empty;

        return (parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString())
               ?? string.Empty;
    }

    private ElementId Find(ElementId id)
    {
        while (_parent[id] != id)
        {
            _parent[id] = _parent[_parent[id]];
            id = _parent[id];
        }

        return id;
    }

    private void Union(ElementId a, ElementId b)
    {
        var rootA = Find(a);
        var rootB = Find(b);
        if (rootA != rootB) _parent[rootA] = rootB;
    }
}
