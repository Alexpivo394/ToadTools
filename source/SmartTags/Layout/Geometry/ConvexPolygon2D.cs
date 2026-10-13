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
