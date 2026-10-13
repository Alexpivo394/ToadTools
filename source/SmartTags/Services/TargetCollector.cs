using SmartTags.Layout.Geometry;
using SmartTags.Models;

namespace SmartTags.Services;

/// <summary>Собирает видимые элементы выбранных категорий и превращает их в цели маркировки.</summary>
internal sealed class TargetCollector(TagView view, SmartTagsSettings settings, Action<string>? log)
{
    // Где на участке пробовать точку привязки (доли длины), в порядке предпочтения.
    private static readonly double[] RunFractions = [0.5, 0.35, 0.65, 0.2, 0.8];

    // Элементы крупнее 8 мм на листе получают дополнительные точки привязки по краям.
    private const double LargeElementMm = 8;

    private readonly ViewProjection _projection = view.Projection;

    public List<Element> CollectElements(CategoryChoice choice)
    {
        var elements = new FilteredElementCollector(view.Document, view.View.Id)
            .OfCategory(choice.Category)
            .WhereElementIsNotElementType()
            .Where(element => settings.OnlyElements is null || settings.OnlyElements.Contains(element.Id))
            .Where(IsVisible);

        if (choice.DuctProfile != DuctProfile.Any)
            elements = elements.Where(element => CategoryScanner.ProfileOf(element) == choice.DuctProfile);

        return elements.ToList();
    }

    public List<TagTarget> BuildTargets(List<Element> elements, CategoryChoice choice)
    {
        var targets = new List<TagTarget>();
        if (choice.IsLinear)
        {
            var runs = new RunBuilder(log).Build(elements.OfType<MEPCurve>());
            targets.AddRange(runs.Select(BuildRun));
            targets.AddRange(elements.Where(element => element is not MEPCurve).Select(BuildPoint));
        }
        else
        {
            targets.AddRange(elements.Select(BuildPoint));
        }

        foreach (var target in targets)
            target.Choice = choice;

        return targets;
    }

    private TagTarget BuildRun(List<MEPCurve> run)
    {
        var target = new TagTarget(run[0], true);
        target.Elements.AddRange(run.Skip(1));
        target.Label = Label(run[0], run.Count);

        // Марку ставим на самый длинный видимый (в пределах подрезки) кусок участка.
        MEPCurve? best = null;
        double bestVisible = -1, bestT0 = 0, bestT1 = 1, total = 0;
        foreach (var curve in run)
        {
            if (curve.Location is not LocationCurve { Curve: { } line }) continue;

            var start = line.GetEndPoint(0);
            var end = line.GetEndPoint(1);
            total += start.DistanceTo(end);
            target.Axis ??= RunBuilder.Direction(curve);

            var a = _projection.ToSheet(start);
            var b = _projection.ToSheet(end);
            if (!ClipToCrop(a, b, out var t0, out var t1)) continue;

            var visible = Vector2D.Distance(a, b) * (t1 - t0);
            if (visible <= bestVisible) continue;

            bestVisible = visible;
            best = curve;
            bestT0 = t0;
            bestT1 = t1;
        }

        target.LengthMm = total * 304.8;
        if (best is null || target.Axis is null)
        {
            target.MarkSkipped(SkipReason.OutsideCrop);
            return target;
        }

        target.Primary = best;
        if (Math.Abs(target.Axis.Z) > 0.95 && !settings.TagVerticalRuns)
        {
            target.MarkSkipped(SkipReason.Vertical);
            return target;
        }

        if (target.LengthMm < settings.MinRunLength)
        {
            target.MarkSkipped(SkipReason.TooShort, $"{Math.Round(target.LengthMm)} мм");
            return target;
        }

        var bestLine = ((LocationCurve)best.Location).Curve;
        var p0 = bestLine.GetEndPoint(0);
        var p1 = bestLine.GetEndPoint(1);
        if (bestVisible < 0.5)
        {
            // На виде участок почти точка (стояк на плане) — одна привязка в середине.
            target.Anchors.Add((p0 + p1) * 0.5);
        }
        else
        {
            foreach (var fraction in RunFractions)
                target.Anchors.Add(p0 + (p1 - p0) * (bestT0 + (bestT1 - bestT0) * fraction));
        }

        target.Depth = _projection.DepthOf(target.Anchors[0]);
        return target;
    }

    private TagTarget BuildPoint(Element element)
    {
        var target = new TagTarget(element, false) { Label = Label(element, 1) };

        var box = element.get_BoundingBox(view.View) ?? element.get_BoundingBox(null);
        var center = box is not null
            ? (box.Transform ?? Transform.Identity).OfPoint((box.Min + box.Max) * 0.5)
            : LocationOf(element);

        if (center is null)
        {
            target.MarkSkipped(SkipReason.NoAnchor);
            return target;
        }

        if (!view.IsInsideCrop(_projection.ToSheet(center)))
        {
            target.MarkSkipped(SkipReason.OutsideCrop);
            return target;
        }

        target.Anchors.Add(center);
        target.Depth = _projection.DepthOf(center);
        if (box is null) return target;

        // Крупному оборудованию — ещё точки у середин сторон, чтобы выноска не тянулась через весь элемент.
        var outline = _projection.Project(box);
        if (outline.Width <= LargeElementMm && outline.Height <= LargeElementMm) return target;

        var inner = outline.Inflate(-Math.Min(1, Math.Min(outline.Width, outline.Height) * 0.2));
        Vector2D[] extra =
        [
            new(inner.MinX, inner.Center.Y), new(inner.MaxX, inner.Center.Y),
            new(inner.Center.X, inner.MaxY), new(inner.Center.X, inner.MinY)
        ];
        foreach (var point in extra.Where(view.IsInsideCrop))
            target.Anchors.Add(_projection.ToModel(point, target.Depth));

        return target;
    }

    private static XYZ? LocationOf(Element element)
    {
        return element.Location switch
        {
            LocationPoint point => point.Point,
            LocationCurve { Curve: { } curve } => curve.Evaluate(0.5, true),
            _ => null
        };
    }

    /// <summary>Видимая в подрезке часть отрезка [a; b] (параметры t0..t1).</summary>
    private bool ClipToCrop(Vector2D a, Vector2D b, out double t0, out double t1)
    {
        t0 = 0;
        t1 = 1;
        if (!settings.UseCrop || view.Crop is not { } crop) return true;

        return Geometry2D.ClipSegment(a, b, crop, out t0, out t1);
    }

    private bool IsVisible(Element element)
    {
        try
        {
            return !element.IsHidden(view.View);
        }
        catch
        {
            return true;
        }
    }

    private static string Label(Element element, int count)
    {
        var id = RevitCompat.IdValue(element.Id).ToString();
        var label = string.IsNullOrEmpty(element.Name) ? id : $"{element.Name} [{id}]";
        return count > 1 ? $"{label} (+{count - 1})" : label;
    }
}
