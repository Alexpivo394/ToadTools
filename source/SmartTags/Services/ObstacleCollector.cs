using SmartTags.Layout;
using SmartTags.Layout.Geometry;
using SmartTags.Models;

namespace SmartTags.Services;

/// <summary>Переводит то, что уже есть на виде (элементы, марки, аннотации), в препятствия для раскладки.</summary>
internal sealed class ObstacleCollector(TagView view, Action<string>? log)
{
    // Всё, что на листе больше 2 м (или 1 м для аннотаций), — скорее ошибка геометрии, чем препятствие.
    private const double MaxElementMm = 2000;
    private const double MaxAnnotationMm = 1000;

    private readonly ViewProjection _projection = view.Projection;

    /// <summary>Инженерные элементы. Своим элементам марок проставляется владелец.</summary>
    public void AddElements(LayoutRequest request, IReadOnlyDictionary<ElementId, int> ownerOf)
    {
        var elements = new FilteredElementCollector(view.Document, view.View.Id)
            .WherePasses(new ElementMulticategoryFilter(TagCategoryMap.ObstacleCategories.ToList()))
            .WhereElementIsNotElementType();

        var count = 0;
        foreach (var element in elements)
        {
            try
            {
                if (element.IsHidden(view.View)) continue;

                var owner = ownerOf.TryGetValue(element.Id, out var item) ? item : -1;
                var label = RevitCompat.IdValue(element.Id).ToString();
                foreach (var shape in Footprints(element))
                {
                    var obstacle = LayoutObstacle.FromPolygon(ObstacleKind.Element, shape, label);
                    obstacle.OwnerItem = owner;
                    request.Obstacles.Add(obstacle);
                    count++;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Препятствие {RevitCompat.IdValue(element.Id)}: {ex.Message}");
            }
        }

        log?.Invoke($"Препятствий-элементов: {count}");
    }

    /// <summary>Контур цели на листе (выпуклая оболочка её элементов).</summary>
    public ConvexPolygon2D? Footprint(TagTarget target)
    {
        var points = target.Elements.SelectMany(element => Footprints(element).SelectMany(shape => shape.Vertices)).ToList();
        if (points.Count < 3) return null;

        var hull = new ConvexPolygon2D(points);
        return hull.IsDegenerate ? null : hull;
    }

    /// <summary>Стоящие марки: текст и выноска (до излома и полка).</summary>
    public void AddTags(LayoutRequest request, IEnumerable<ExistingTag> tags)
    {
        var count = 0;
        foreach (var tag in tags.Where(tag => tag.IsObstacle && !tag.Text.IsEmpty))
        {
            var text = tag.Text;
            var label = "марка " + RevitCompat.IdValue(tag.Tag.Id);
            if (text.Width < MaxAnnotationMm && text.Height < MaxAnnotationMm)
            {
                request.Obstacles.Add(LayoutObstacle.FromBox(ObstacleKind.TagText, text, label));
                count++;
            }

            if (tag.End is null) continue;

            var end = _projection.ToSheet(tag.End);
            var turn = tag.Elbow is not null ? _projection.ToSheet(tag.Elbow) : end;
            var attach = Vector2D.Distance(turn, text.LeftMiddle) <= Vector2D.Distance(turn, text.RightMiddle)
                ? text.LeftMiddle
                : text.RightMiddle;

            if (tag.Elbow is not null)
                request.Obstacles.Add(LayoutObstacle.FromSegment(ObstacleKind.TagLeader, new Segment2D(end, turn), label));
            request.Obstacles.Add(LayoutObstacle.FromSegment(ObstacleKind.TagLeader, new Segment2D(turn, attach), label));
        }

        log?.Invoke($"Препятствий-марок: {count}");
    }

    /// <summary>Текстовые примечания, марки помещений/пространств, типовые аннотации.</summary>
    public void AddAnnotations(LayoutRequest request)
    {
        var annotations = new FilteredElementCollector(view.Document, view.View.Id)
            .OfClass(typeof(TextNote))
            .Concat(new FilteredElementCollector(view.Document, view.View.Id).OfClass(typeof(SpatialElementTag)))
            .Concat(new FilteredElementCollector(view.Document, view.View.Id)
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .WhereElementIsNotElementType());

        var count = 0;
        foreach (var element in annotations)
        {
            try
            {
                if (element.IsHidden(view.View) || element.get_BoundingBox(view.View) is not { } box) continue;

                var outline = _projection.Project(box);
                if (outline.IsEmpty || outline.Width > MaxAnnotationMm || outline.Height > MaxAnnotationMm) continue;

                request.Obstacles.Add(LayoutObstacle.FromBox(ObstacleKind.Annotation, outline,
                    "аннотация " + RevitCompat.IdValue(element.Id)));
                count++;
            }
            catch
            {
                // Аннотация без рамки — пропускаем.
            }
        }

        log?.Invoke($"Препятствий-аннотаций: {count}");
    }

    private List<ConvexPolygon2D> Footprints(Element element)
    {
        if (element is MEPCurve curve && CurveFootprints(curve) is { Count: > 0 } strips) return strips;

        if (element.get_BoundingBox(view.View) is not { } box) return [];

        var hull = _projection.ProjectHull(box);
        if (hull.IsDegenerate || hull.Bounds.Width > MaxElementMm || hull.Bounds.Height > MaxElementMm) return [];
        return [hull];
    }

    /// <summary>Труба/воздуховод — полосы вдоль оси шириной видимого сечения (с изоляцией).</summary>
    private List<ConvexPolygon2D> CurveFootprints(MEPCurve curve)
    {
        if (curve.Location is not LocationCurve { Curve: { } line }) return [];

        var points = line.Tessellate();
        if (points.Count < 2) return [];

        CrossSection(curve, out var width, out var height, out var round);
        var strips = new List<ConvexPolygon2D>();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var direction = points[i + 1] - points[i];
            if (direction.GetLength() < 1e-9) continue;

            var a = _projection.ToSheet(points[i]);
            var b = _projection.ToSheet(points[i + 1]);
            if (Vector2D.Distance(a, b) < 0.3)
            {
                // Кусок смотрит на нас торцом (стояк на плане) — квадрат по наибольшему размеру.
                var size = _projection.ModelToSheet(Math.Max(width, height));
                strips.Add(new ConvexPolygon2D(Box2D.FromCenter(a, size, size).Corners()));
                continue;
            }

            strips.Add(ConvexPolygon2D.Strip(a, b, Math.Max(0.4, 2 * _projection.ModelToSheet(
                VisibleHalfWidth(direction.Normalize(), a, b, width, height, round)))));
        }

        return strips;
    }

    /// <summary>Полуширина сечения, видимая поперёк оси на виде (у прямоугольного зависит от поворота).</summary>
    private double VisibleHalfWidth(XYZ axis, Vector2D a, Vector2D b, double width, double height, bool round)
    {
        if (round) return width / 2;

        var across = (b - a).Normalize().Perpendicular();
        var acrossModel = _projection.Right * across.X + _projection.Up * across.Y;
        var side = Math.Abs(axis.Z) > 0.99 ? XYZ.BasisX : axis.CrossProduct(XYZ.BasisZ).Normalize();
        var top = axis.CrossProduct(side).Normalize();
        return Math.Abs(width / 2 * side.DotProduct(acrossModel)) + Math.Abs(height / 2 * top.DotProduct(acrossModel));
    }

    private static void CrossSection(MEPCurve curve, out double width, out double height, out bool round)
    {
        width = height = 0;
        round = false;
        try
        {
            if (curve.Diameter > 1e-9)
            {
                width = height = curve.Diameter;
                round = true;
            }
        }
        catch
        {
            // Не круглое сечение.
        }

        if (!round)
        {
            try
            {
                width = curve.Width;
                height = curve.Height;
            }
            catch
            {
                // Нет ширины/высоты (например, у некоторых гибких элементов).
            }
        }

        var insulation = curve.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_THICKNESS);
        if (insulation is { StorageType: StorageType.Double } && insulation.AsDouble() > 0)
        {
            width += 2 * insulation.AsDouble();
            height += 2 * insulation.AsDouble();
        }

        if (width <= 1e-9 && height <= 1e-9 && curve.get_BoundingBox(null) is { } box)
        {
            var size = box.Max - box.Min;
            width = height = Math.Min(Math.Abs(size.X), Math.Min(Math.Abs(size.Y), Math.Abs(size.Z)));
        }

        if (width <= 1e-9) width = height;
        if (height <= 1e-9) height = width;
    }
}
