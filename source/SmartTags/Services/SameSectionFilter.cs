using SmartTags.Layout;
using SmartTags.Layout.Geometry;
using SmartTags.Models;

namespace SmartTags.Services;

/// <summary>
///     Участки, соединённые фитингами (отводами, тройниками), несут одну и ту же информацию, если у них
///     одинаковое сечение и расход (воздуховоды) или сечение и система (трубы). Марку получает один из них:
///     тот, что стоит свободнее (не в пучке параллельных труб), при равенстве — самый длинный.
/// </summary>
internal static class SameSectionFilter
{
    // Чужой участок ближе этого к точке привязки (мм на листе) считается соседом по пучку.
    private const double CrowdRadiusMm = 4;

    public static void Apply(List<TagTarget> targets, ViewProjection projection, Action<string>? log)
    {
        var runs = Enumerable.Range(0, targets.Count)
            .Where(i => targets[i].IsRun && targets[i].Elements.Any(IsSupported))
            .ToList();
        if (runs.Count < 2) return;

        var runOfElement = new Dictionary<ElementId, int>();
        foreach (var index in runs)
        foreach (var element in targets[index].Elements.Where(IsSupported))
            runOfElement[element.Id] = index;

        var sets = new DisjointSets(targets.Count);
        foreach (var index in runs)
        foreach (var curve in targets[index].Elements.OfType<MEPCurve>())
        foreach (var neighbor in NeighborsThroughFittings(curve))
        {
            if (runOfElement.TryGetValue(neighbor.Id, out var other) && other != index && SameSection(curve, neighbor))
                sets.Union(index, other);
        }

        var segments = runs.ToDictionary(index => index, index => Segment(targets[index], projection));
        var skipped = 0;
        foreach (var group in runs.GroupBy(sets.Find).Where(group => group.Count() > 1))
        {
            var keeper = group
                .OrderBy(i => targets[i].IsSkipped)
                .ThenBy(i => Crowd(targets[i], i, segments, projection))
                .ThenByDescending(i => targets[i].LengthMm)
                .First();

            foreach (var index in group.Where(i => i != keeper && !targets[i].IsSkipped))
            {
                targets[index].MarkSkipped(SkipReason.SameSection);
                skipped++;
            }
        }

        log?.Invoke($"Одинаковое сечение: пропущено {skipped} из {runs.Count} участков труб и воздуховодов");
    }

    /// <summary>Сколько других участков проходит рядом с точкой привязки — в пучке марку не разобрать.</summary>
    private static int Crowd(TagTarget target, int self, Dictionary<int, (Vector2D A, Vector2D B)?> segments,
        ViewProjection projection)
    {
        if (target.Anchors.Count == 0) return int.MaxValue;

        var anchor = projection.ToSheet(target.Anchors[0]);
        var crowd = 0;
        foreach (var pair in segments)
        {
            if (pair.Key == self || pair.Value is not { } segment) continue;
            if (Geometry2D.DistanceToSegment(anchor, segment.A, segment.B) <= CrowdRadiusMm) crowd++;
        }

        return crowd;
    }

    private static (Vector2D A, Vector2D B)? Segment(TagTarget target, ViewProjection projection)
    {
        if (target.Primary.Location is not LocationCurve { Curve: { } line }) return null;
        return (projection.ToSheet(line.GetEndPoint(0)), projection.ToSheet(line.GetEndPoint(1)));
    }

    private static IEnumerable<MEPCurve> NeighborsThroughFittings(MEPCurve curve)
    {
        var seen = new HashSet<ElementId>();
        foreach (var connector in RunBuilder.EndConnectors(curve.ConnectorManager))
        foreach (Connector linked in connector.AllRefs)
        {
            var owner = linked.Owner;
            if (owner is null || owner.Id == curve.Id) continue;

            if (owner is MEPCurve neighbor)
            {
                if (seen.Add(neighbor.Id)) yield return neighbor;
                continue;
            }

            if (owner is not FamilyInstance { MEPModel: { } model } || !IsFitting(owner)) continue;

            foreach (var other in RunBuilder.EndConnectors(model.ConnectorManager))
            foreach (Connector far in other.AllRefs)
            {
                if (far.Owner is MEPCurve next && next.Id != curve.Id && seen.Add(next.Id))
                    yield return next;
            }
        }
    }

    private static bool SameSection(MEPCurve a, MEPCurve b)
    {
        if (IsDuct(a) != IsDuct(b) || RunBuilder.SizeKey(a) != RunBuilder.SizeKey(b)) return false;
        if (!IsDuct(a)) return SystemName(a) == SystemName(b);

        var flowA = Flow(a);
        var flowB = Flow(b);
        var tolerance = Math.Max(1e-8, 0.002 * Math.Max(Math.Abs(flowA), Math.Abs(flowB)));
        return Math.Abs(flowA - flowB) <= tolerance;
    }

    private static double Flow(Element element)
    {
        var parameter = element.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);
        return parameter is { HasValue: true } ? parameter.AsDouble() : 0;
    }

    private static string SystemName(Element element)
    {
        return element.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? string.Empty;
    }

    private static bool IsSupported(Element element) => IsDuct(element) || IsPipe(element);
    private static bool IsDuct(Element element) => RevitCompat.Is(element, BuiltInCategory.OST_DuctCurves);
    private static bool IsPipe(Element element) => RevitCompat.Is(element, BuiltInCategory.OST_PipeCurves);

    private static bool IsFitting(Element element)
    {
        return RevitCompat.Is(element, BuiltInCategory.OST_DuctFitting) ||
               RevitCompat.Is(element, BuiltInCategory.OST_PipeFitting);
    }
}
