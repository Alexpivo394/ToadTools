using SmartTags.Layout.Geometry;
using SmartTags.Models;

namespace SmartTags.Services;

/// <summary>Активный вид, на котором расставляются марки: тип, плоскость, подрезка.</summary>
public sealed class TagView
{
    private TagView(Document document, View view, ViewKind kind)
    {
        Document = document;
        View = view;
        Kind = kind;
        Name = view.Name;
        Scale = view.Scale;
        Projection = new ViewProjection(view);
        IsUnlocked3D = view is View3D { IsLocked: false };
        ReadCrop();
    }

    public Document Document { get; }
    public View View { get; }
    public ViewKind Kind { get; }
    public string Name { get; }
    public int Scale { get; }
    public ViewProjection Projection { get; }

    /// <summary>Марки на 3D-виде можно ставить только после блокировки ориентации.</summary>
    public bool IsUnlocked3D { get; }

    /// <summary>Подрезка вида в координатах листа (null — подрезка выключена).</summary>
    public Box2D? Crop { get; private set; }

    public ConvexPolygon2D? CropShape { get; private set; }

    public string KindText => Kind switch
    {
        ViewKind.Plan => "план",
        ViewKind.Section => "разрез/фасад",
        _ => "3D"
    };

    public string Description => $"{Name} · 1:{Scale} · {KindText}";

    /// <summary>Проверяет вид. При неподходящем виде возвращает null и причину.</summary>
    public static TagView? TryCreate(Document document, View? view, out string? error)
    {
        error = null;
        if (view is null)
        {
            error = "Нет активного вида.";
            return null;
        }

        if (view.IsTemplate)
        {
            error = "Активен шаблон вида.";
            return null;
        }

        ViewKind kind;
        switch (view.ViewType)
        {
            case ViewType.FloorPlan:
            case ViewType.CeilingPlan:
            case ViewType.EngineeringPlan:
            case ViewType.AreaPlan:
                kind = ViewKind.Plan;
                break;
            case ViewType.Elevation:
            case ViewType.Section:
            case ViewType.Detail:
                kind = ViewKind.Section;
                break;
            case ViewType.ThreeD:
                kind = ViewKind.ThreeD;
                break;
            default:
                error = $"Марки расставляются только на планах, разрезах, фасадах и 3D-видах. Текущий вид: {view.ViewType}.";
                return null;
        }

        if (view is View3D { IsPerspective: true })
        {
            error = "На перспективном 3D-виде марки не ставятся. Переключите вид в ортогональный режим.";
            return null;
        }

        try
        {
            return new TagView(document, view, kind);
        }
        catch (Exception ex)
        {
            error = "Не удалось определить плоскость вида: " + ex.Message;
            return null;
        }
    }

    /// <summary>Пересоздаёт контекст для другого вида (после блокировки 3D-вида копией).</summary>
    public TagView ForView(View view) => new(Document, view, Kind);

    /// <summary>
    ///     Точка элемента там, где он нарисован на виде: элементы, сдвинутые «Переместить элементы», показываются
    ///     со смещением, а их линия расположения остаётся в модели. Рамки (get_BoundingBox вида) Revit уже сдвигает сам.
    /// </summary>
    public XYZ Displaced(Element element, XYZ point)
    {
        _displacements ??= ReadDisplacements();
        return _displacements.TryGetValue(element.Id, out var offset) ? point + offset : point;
    }

    private Dictionary<ElementId, XYZ>? _displacements;

    private Dictionary<ElementId, XYZ> ReadDisplacements()
    {
        var result = new Dictionary<ElementId, XYZ>();
        try
        {
            foreach (var displacement in new FilteredElementCollector(Document, View.Id)
                         .OfClass(typeof(DisplacementElement))
                         .Cast<DisplacementElement>())
            {
                var offset = displacement.GetAbsoluteDisplacement();
                foreach (var id in displacement.GetDisplacedElementIds())
                    result[id] = offset;
            }
        }
        catch
        {
            // Вид без смещений (или не поддерживает их).
        }

        return result;
    }

    public bool IsInsideCrop(Vector2D point)
    {
        if (Crop is not { } crop) return true;
        return crop.Contains(point) && (CropShape is null || CropShape.Contains(point));
    }

    private void ReadCrop()
    {
        if (!View.CropBoxActive || View.CropBox is not { } cropBox) return;

        // Рамка подрезки задана в своей системе координат: углы на дальней плоскости.
        var transform = cropBox.Transform ?? Transform.Identity;
        var crop = Box2D.Empty;
        foreach (var x in new[] { cropBox.Min.X, cropBox.Max.X })
        foreach (var y in new[] { cropBox.Min.Y, cropBox.Max.Y })
            crop = crop.Union(Projection.ToSheet(transform.OfPoint(new XYZ(x, y, cropBox.Min.Z))));

        // Непрямоугольная подрезка — уточняем её контуром.
        try
        {
            var loops = View.GetCropRegionShapeManager()?.GetCropShape();
            if (loops is { Count: 1 })
            {
                var points = loops[0].SelectMany(curve => curve.Tessellate()).Select(Projection.ToSheet).ToList();
                var shape = points.Count >= 3 ? new ConvexPolygon2D(points) : null;
                if (shape is { IsDegenerate: false })
                {
                    CropShape = shape;
                    if (crop.IsEmpty) crop = shape.Bounds;
                }
            }
        }
        catch
        {
            // У некоторых видов (например, 3D) менеджера формы подрезки нет — хватит рамки.
        }

        if (!crop.IsEmpty) Crop = crop;
    }
}
