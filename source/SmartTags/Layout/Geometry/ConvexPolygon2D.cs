namespace SmartTags.Layout.Geometry;

/// <summary>Выпуклый многоугольник: строится как оболочка переданных точек.</summary>
public sealed class ConvexPolygon2D
{
    private readonly Vector2D[] _vertices;

    public ConvexPolygon2D(IEnumerable<Vector2D> points)
    {
        _vertices = Geometry2D.ConvexHull(points).ToArray();

        var bounds = Box2D.Empty;
        foreach (var vertex in _vertices)
            bounds = bounds.Union(vertex);
        Bounds = bounds;
    }

    public IReadOnlyList<Vector2D> Vertices => _vertices;
    public Box2D Bounds { get; }
    public bool IsDegenerate => _vertices.Length < 3;

    /// <summary>Полоса шириной <paramref name="width" /> вдоль отрезка [a; b].</summary>
    public static ConvexPolygon2D Strip(Vector2D a, Vector2D b, double width)
    {
        var axis = b - a;
        if (axis.Length < 1e-9)
            return new ConvexPolygon2D(Box2D.FromCenter(a, width, width).Corners());

        var side = axis.Normalize().Perpendicular() * (width / 2);
        return new ConvexPolygon2D([a - side, b - side, b + side, a + side]);
    }

    /// <summary>Наименьшая ширина многоугольника (по всем направлениям его сторон).</summary>
    public double Thickness
    {
        get
        {
            if (IsDegenerate) return 0;

            var thickness = double.MaxValue;
            for (var i = 0; i < _vertices.Length; i++)
            {
                var edge = _vertices[(i + 1) % _vertices.Length] - _vertices[i];
                if (edge.Length < 1e-9) continue;

                var normal = edge.Normalize().Perpendicular();
                var far = 0.0;
                foreach (var vertex in _vertices)
                    far = Math.Max(far, Math.Abs(Vector2D.Dot(vertex - _vertices[i], normal)));
                thickness = Math.Min(thickness, far);
            }

            return thickness == double.MaxValue ? 0 : thickness;
        }
    }

    /// <summary>Площадь общей части с прямоугольником (многоугольник обрезается по его сторонам).</summary>
    public double IntersectionArea(Box2D box)
    {
        if (IsDegenerate || !Bounds.Overlaps(box)) return 0;

        var clipped = new List<Vector2D>(_vertices);
        clipped = Clip(clipped, p => p.X - box.MinX, (a, b) => Lerp(a, b, (box.MinX - a.X) / (b.X - a.X)));
        clipped = Clip(clipped, p => box.MaxX - p.X, (a, b) => Lerp(a, b, (box.MaxX - a.X) / (b.X - a.X)));
        clipped = Clip(clipped, p => p.Y - box.MinY, (a, b) => Lerp(a, b, (box.MinY - a.Y) / (b.Y - a.Y)));
        clipped = Clip(clipped, p => box.MaxY - p.Y, (a, b) => Lerp(a, b, (box.MaxY - a.Y) / (b.Y - a.Y)));
        if (clipped.Count < 3) return 0;

        var area = 0.0;
        for (var i = 0; i < clipped.Count; i++)
            area += Vector2D.Cross(clipped[i], clipped[(i + 1) % clipped.Count]);
        return Math.Abs(area) / 2;
    }

    /// <summary>Отсечение по полуплоскости inside(p) ≥ 0 (Сазерленд — Ходжман).</summary>
    private static List<Vector2D> Clip(List<Vector2D> points, Func<Vector2D, double> inside,
        Func<Vector2D, Vector2D, Vector2D> cross)
    {
        var result = new List<Vector2D>(points.Count + 2);
        for (var i = 0; i < points.Count; i++)
        {
            var current = points[i];
            var next = points[(i + 1) % points.Count];
            var currentIn = inside(current) >= 0;
            var nextIn = inside(next) >= 0;
            if (currentIn) result.Add(current);
            if (currentIn != nextIn) result.Add(cross(current, next));
        }

        return result;
    }

    private static Vector2D Lerp(Vector2D a, Vector2D b, double t) => a + (b - a) * t;

    public bool Contains(Vector2D p)
    {
        if (IsDegenerate || !Bounds.Contains(p)) return false;

        for (var i = 0; i < _vertices.Length; i++)
        {
            var edge = _vertices[(i + 1) % _vertices.Length] - _vertices[i];
            if (Vector2D.Cross(edge, p - _vertices[i]) < -1e-9) return false;
        }

        return true;
    }

    public bool Overlaps(Box2D box)
    {
        return !IsDegenerate && Bounds.Overlaps(box) && Geometry2D.ConvexOverlap(_vertices, box.Corners());
    }

    public bool Overlaps(ConvexPolygon2D other)
    {
        return !IsDegenerate && !other.IsDegenerate && Bounds.Overlaps(other.Bounds) &&
               Geometry2D.ConvexOverlap(_vertices, other._vertices);
    }

    public bool Hits(Segment2D segment, double tolerance)
    {
        if (IsDegenerate || !Bounds.Overlaps(segment.Bounds.Inflate(tolerance))) return false;
        if (Contains(segment.Start) || Contains(segment.End)) return true;

        for (var i = 0; i < _vertices.Length; i++)
        {
            if (Geometry2D.SegmentsCross(_vertices[i], _vertices[(i + 1) % _vertices.Length],
                    segment.Start, segment.End, tolerance))
                return true;
        }

        return false;
    }
}
