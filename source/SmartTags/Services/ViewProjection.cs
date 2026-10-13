using SmartTags.Layout.Geometry;

namespace SmartTags.Services;

/// <summary>
///     Перевод между моделью и плоскостью вида. Координаты на плоскости — мм на листе
///     (с учётом масштаба вида), чтобы зазоры и длины выносок задавались как на чертеже.
/// </summary>
public sealed class ViewProjection
{
    private const double MmPerFoot = 304.8;

    private readonly XYZ _origin;
    private readonly double _scale;

    public ViewProjection(View view)
    {
        _origin = view.Origin;
        Right = view.RightDirection.Normalize();
        Up = view.UpDirection.Normalize();
        _scale = Math.Max(1, view.Scale);
    }

    public XYZ Right { get; }
    public XYZ Up { get; }
    public XYZ Normal => Right.CrossProduct(Up);

    public double SheetToModel(double mm) => mm * _scale / MmPerFoot;
    public double ModelToSheet(double feet) => feet * MmPerFoot / _scale;

    public Vector2D ToSheet(XYZ point)
    {
        var offset = point - _origin;
        return new Vector2D(ModelToSheet(offset.DotProduct(Right)), ModelToSheet(offset.DotProduct(Up)));
    }

    /// <summary>Глубина точки вдоль нормали к плоскости вида.</summary>
    public double DepthOf(XYZ point) => (point - _origin).DotProduct(Normal);

    public XYZ ToModel(Vector2D sheet, double depth)
    {
        return _origin + Right * SheetToModel(sheet.X) + Up * SheetToModel(sheet.Y) + Normal * depth;
    }

    public Vector2D DirectionOnSheet(XYZ direction)
    {
        return new Vector2D(direction.DotProduct(Right), direction.DotProduct(Up)).Normalize();
    }

    public Box2D Project(BoundingBoxXYZ box)
    {
        var result = Box2D.Empty;
        foreach (var corner in Corners(box))
            result = result.Union(ToSheet(corner));
        return result;
    }

    public ConvexPolygon2D ProjectHull(BoundingBoxXYZ box) => new(Corners(box).Select(ToSheet));

    /// <summary>
    ///     Размер марки на 3D-виде. Её рамка выровнена по осям модели, и простая проекция углов
    ///     завышает размер. Ищем ширину w и высоту h такие, что w·|Right| + h·|Up| ≈ размах рамки.
    /// </summary>
    public bool TryGetPlanarSize(BoundingBoxXYZ box, out double width, out double height)
    {
        var transform = box.Transform ?? Transform.Identity;
        var min = transform.OfPoint(box.Min);
        var max = transform.OfPoint(box.Max);
        double[] extent = [Math.Abs(max.X - min.X), Math.Abs(max.Y - min.Y), Math.Abs(max.Z - min.Z)];
        double[] right = [Math.Abs(Right.X), Math.Abs(Right.Y), Math.Abs(Right.Z)];
        double[] up = [Math.Abs(Up.X), Math.Abs(Up.Y), Math.Abs(Up.Z)];

        double rr = 0, ru = 0, uu = 0, re = 0, ue = 0;
        for (var i = 0; i < 3; i++)
        {
            rr += right[i] * right[i];
            ru += right[i] * up[i];
            uu += up[i] * up[i];
            re += right[i] * extent[i];
            ue += up[i] * extent[i];
        }

        var determinant = rr * uu - ru * ru;
        if (Math.Abs(determinant) > 1e-9)
        {
            var w = (re * uu - ue * ru) / determinant;
            var h = (rr * ue - ru * re) / determinant;
            if (w > 1e-9 && h > 1e-9)
            {
                width = ModelToSheet(w);
                height = ModelToSheet(h);
                return true;
            }
        }

        // Оси вида совпадают с осями модели (или решение вырождено) — проекция точна.
        var projected = Project(box);
        width = projected.Width;
        height = projected.Height;
        return width > 1e-6 && height > 1e-6;
    }

    public static IEnumerable<XYZ> Corners(BoundingBoxXYZ box)
    {
        var transform = box.Transform ?? Transform.Identity;
        for (var i = 0; i < 8; i++)
        {
            yield return transform.OfPoint(new XYZ(
                (i & 1) == 0 ? box.Min.X : box.Max.X,
                (i & 2) == 0 ? box.Min.Y : box.Max.Y,
                (i & 4) == 0 ? box.Min.Z : box.Max.Z));
        }
    }
}
