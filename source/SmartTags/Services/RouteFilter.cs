using SmartTags.Layout;
using SmartTags.Layout.Geometry;
using SmartTags.Models;

namespace SmartTags.Services;

/// <summary>
///     Трассы: участки одной категории, размера и системы, соединённые отводами, муфтами, арматурой или
///     прямым проходом тройника (ответвление — уже другая трасса). Трасса с одинаковым текстом марки
///     подписывается один раз — на участке, который стоит свободнее (не в пучке параллельных труб), при
///     равенстве — на самом длинном. Если марка показывает то, что вдоль трассы меняется (например, расход),
///     подпись получает каждая часть трассы со своим текстом. Части трассы на разной высоте (подъём, опуск,
///     утка) подписываются каждая отдельно: на плане и разрезе они читаются как разные трубы.
/// </summary>
internal static class RouteFilter
{
    // Чужой участок ближе этого к точке привязки (мм на листе) считается соседом по пучку.
    private const double CrowdRadiusMm = 4;

    // Через сколько фитингов подряд (отвод к отводу без трубы между ними) искать продолжение трассы.
    private const int MaxFittingChain = 4;

    // Соединители тройника считаются соосными, если угол между ними больше ~170°.
    private const double OppositeCos = -0.985;

    // Участки на одной высоте, если их отметки расходятся не больше чем на 50 мм (в футах).
    private const double LevelTolerance = 50 / 304.8;

    // Участок круче ~30° к горизонтали — стояк или подъём: он не связывает части трассы на разной высоте.
    private const double SteepSin = 0.5;

    public static void Apply(List<TagTarget> targets, TagView view, double minLength, Action<string>? log)
    {
        var runs = Enumerable.Range(0, targets.Count)
            .Where(i => targets[i].IsRun && targets[i].Elements.Any(element => element is MEPCurve))
            .ToList();
        if (runs.Count == 0) return;

        var runOfElement = new Dictionary<ElementId, int>();
        foreach (var index in runs)
        foreach (var element in targets[index].Elements)
            runOfElement[element.Id] = index;

        var levels = runs.ToDictionary(index => index, index => Level(targets[index]));
        var sets = new DisjointSets(targets.Count);
        foreach (var index in runs)
        foreach (var curve in targets[index].Elements.OfType<MEPCurve>())
        foreach (var neighbor in Neighbors(curve))
        {
            if (!runOfElement.TryGetValue(neighbor.Id, out var other) || other == index) continue;
            if (targets[other].Choice == targets[index].Choice &&
                RunBuilder.RunKey(curve) == RunBuilder.RunKey(neighbor) &&
                SameLevel(levels[index], levels[other]))
                sets.Union(index, other);
        }

        var segments = runs.ToDictionary(index => index, index => Segment(targets[index], view));
        int routes = 0, skipped = 0, tooShort = 0;
        foreach (var route in runs.GroupBy(sets.Find).Select(group => group.ToList()))
        {
            routes++;
            var active = route.Where(i => !targets[i].IsSkipped).ToList();
            if (active.Count == 0) continue;

            if (route.Sum(i => targets[i].LengthMm) < minLength)
            {
                foreach (var index in active)
                {
                    targets[index].MarkSkipped(SkipReason.TooShort, $"трасса {Math.Round(route.Sum(i => targets[i].LengthMm))} мм");
                    tooShort++;
                }

                continue;
            }

            // Трасса уже подписана (режим «пропустить подписанные») — новых марок на ней не нужно.
            var tagged = route.Any(i => targets[i].Skip == SkipReason.AlreadyTagged);
            foreach (var same in active.GroupBy(i => targets[i].Text))
            {
                // Стоящие марки (режим «переразместить») остаются все: их не убираем, а переставляем.
                var keepers = same.Where(i => targets[i].ExistingTag is not null).ToList();
                if (keepers.Count == 0 && !tagged)
                    keepers.Add(same
                        .OrderBy(i => Crowd(targets[i], i, segments, view.Projection))
                        .ThenByDescending(i => targets[i].LengthMm)
                        .First());

                foreach (var index in same.Where(i => !keepers.Contains(i)))
                {
                    targets[index].MarkSkipped(SkipReason.SameSection);
                    skipped++;
                }
            }
        }

        log?.Invoke($"Трассы: {routes} из {runs.Count} участков, повторов пропущено {skipped}, коротких трасс {tooShort}");
    }

    /// <summary>
    ///     Трубы и воздуховоды, продолжающие трассу за концами <paramref name="curve" />: напрямую или через
    ///     фитинги и арматуру. В тройнике и крестовине трасса идёт только прямо.
    /// </summary>
    private static IEnumerable<MEPCurve> Neighbors(MEPCurve curve)
    {
        var seen = new HashSet<ElementId> { curve.Id };
        foreach (var connector in RunBuilder.EndConnectors(curve.ConnectorManager))
        foreach (Connector linked in connector.AllRefs)
        {
            if (linked.Owner is null || linked.Owner.Id == curve.Id) continue;

            foreach (var next in Through(linked, MaxFittingChain))
            {
                if (seen.Add(next.Id)) yield return next;
            }
        }
    }

    /// <summary>Куда ведёт соединитель <paramref name="entry" />: в трубу — она, в фитинг — дальше сквозь него.</summary>
    private static IEnumerable<MEPCurve> Through(Connector entry, int depth)
    {
        var owner = entry.Owner;
        if (owner is MEPCurve curve)
        {
            yield return curve;
            yield break;
        }

        if (depth == 0 || owner is not FamilyInstance { MEPModel: { } model } || !IsPassThrough(owner)) yield break;

        var ends = RunBuilder.EndConnectors(model.ConnectorManager).ToList();
        foreach (var exit in ends)
        {
            if (exit.Id == entry.Id || (ends.Count > 2 && !Opposite(entry, exit))) continue;

            foreach (Connector next in exit.AllRefs)
            {
                if (next.Owner is null || next.Owner.Id == owner.Id) continue;

                foreach (var found in Through(next, depth - 1))
                    yield return found;
            }
        }
    }

    private static bool Opposite(Connector a, Connector b)
    {
        try
        {
            return a.CoordinateSystem.BasisZ.DotProduct(b.CoordinateSystem.BasisZ) < OppositeCos;
        }
        catch
        {
            // У соединителя нет системы координат — направление не определить, считаем ответвлением.
            return false;
        }
    }

    private static bool IsPassThrough(Element element)
    {
        return RevitCompat.Is(element, BuiltInCategory.OST_DuctFitting) ||
               RevitCompat.Is(element, BuiltInCategory.OST_PipeFitting) ||
               RevitCompat.Is(element, BuiltInCategory.OST_CableTrayFitting) ||
               RevitCompat.Is(element, BuiltInCategory.OST_ConduitFitting) ||
               RevitCompat.Is(element, BuiltInCategory.OST_DuctAccessory) ||
               RevitCompat.Is(element, BuiltInCategory.OST_PipeAccessory);
    }

    /// <summary>Отметки участка (низ и верх, футы) и признак стояка; null — геометрии нет.</summary>
    private static (double Min, double Max, bool Steep)? Level(TagTarget target)
    {
        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var element in target.Elements)
        {
            if (element.Location is not LocationCurve { Curve: { } line }) continue;

            for (var end = 0; end < 2; end++)
            {
                var z = line.GetEndPoint(end).Z;
                min = Math.Min(min, z);
                max = Math.Max(max, z);
            }
        }

        if (min > max) return null;
        return (min, max, target.Axis is { } axis && Math.Abs(axis.Z) > SteepSin);
    }

    /// <summary>
    ///     Соседние участки — одна часть трассы: оба горизонтальные (или с уклоном) на одной высоте либо оба
    ///     стояки. Уклонные участки подряд стыкуются концами, поэтому их отметки перекрываются.
    /// </summary>
    private static bool SameLevel((double Min, double Max, bool Steep)? a, (double Min, double Max, bool Steep)? b)
    {
        if (a is not { } first || b is not { } second) return true;
        if (first.Steep || second.Steep) return first.Steep == second.Steep;

        return first.Min - LevelTolerance <= second.Max && second.Min - LevelTolerance <= first.Max;
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

    private static (Vector2D A, Vector2D B)? Segment(TagTarget target, TagView view)
    {
        if (target.Primary.Location is not LocationCurve { Curve: { } line }) return null;
        return (view.Projection.ToSheet(view.Displaced(target.Primary, line.GetEndPoint(0))),
            view.Projection.ToSheet(view.Displaced(target.Primary, line.GetEndPoint(1))));
    }
}
