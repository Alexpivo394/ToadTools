using SmartTags.Layout;
using SmartTags.Models;

namespace SmartTags.Services;

/// <summary>
///     Участки воздуховодов, соединённые фитингами (отводами и т. п.), с одинаковыми расходом и сечением
///     несут одну и ту же информацию — марку получает только самый длинный из них.
/// </summary>
internal static class SameSectionFilter
{
    public static void Apply(List<TagTarget> targets, Action<string>? log)
    {
        var ductRuns = Enumerable.Range(0, targets.Count)
            .Where(i => targets[i].IsRun && targets[i].Elements.Any(IsDuct))
            .ToList();
        if (ductRuns.Count < 2) return;

        var runOfElement = new Dictionary<ElementId, int>();
        foreach (var index in ductRuns)
        foreach (var element in targets[index].Elements.Where(IsDuct))
            runOfElement[element.Id] = index;

        var sets = new DisjointSets(targets.Count);
        foreach (var index in ductRuns)
        foreach (var curve in targets[index].Elements.OfType<MEPCurve>())
        foreach (var neighbor in NeighborsThroughFittings(curve))
        {
            if (runOfElement.TryGetValue(neighbor.Id, out var other) && other != index &&
                SameFlowAndSize(curve, neighbor))
                sets.Union(index, other);
        }

        var skipped = 0;
        foreach (var group in ductRuns.GroupBy(sets.Find).Where(group => group.Count() > 1))
        {
            var keeper = group
                .OrderBy(i => targets[i].IsSkipped)
                .ThenByDescending(i => targets[i].LengthMm)
                .First();

            foreach (var index in group.Where(i => i != keeper && !targets[i].IsSkipped))
            {
                targets[index].MarkSkipped(SkipReason.SameSection);
                skipped++;
            }
        }

        log?.Invoke($"Одинаковые расход и сечение: пропущено {skipped} из {ductRuns.Count} участков воздуховодов");
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

            if (owner is not FamilyInstance { MEPModel: { } model } || !RevitCompat.Is(owner, BuiltInCategory.OST_DuctFitting))
                continue;

            foreach (var other in RunBuilder.EndConnectors(model.ConnectorManager))
            foreach (Connector far in other.AllRefs)
            {
                if (far.Owner is MEPCurve next && next.Id != curve.Id && seen.Add(next.Id))
                    yield return next;
            }
        }
    }

    private static bool SameFlowAndSize(MEPCurve a, MEPCurve b)
    {
        if (RunBuilder.SizeKey(a) != RunBuilder.SizeKey(b)) return false;

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

    private static bool IsDuct(Element element) => RevitCompat.Is(element, BuiltInCategory.OST_DuctCurves);
}
