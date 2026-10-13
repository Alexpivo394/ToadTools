namespace SmartTags.Layout.Geometry;

public static class Geometry2D
{
    private const double Epsilon = 1e-9;

    /// <summary>
    ///     Пересекаются ли отрезки «по-настоящему». Общая концевая точка (полка, выходящая из выноски,
    ///     две выноски из одной точки) и касание в пределах допуска пересечением не считаются.
    /// </summary>
    public static bool SegmentsCross(Vector2D a1, Vector2D a2, Vector2D b1, Vector2D b2, double tolerance)
    {
        if (a1.IsNear(b1, tolerance) || a1.IsNear(b2, tolerance) ||
            a2.IsNear(b1, tolerance) || a2.IsNear(b2, tolerance))
            return false;

        var da = a2 - a1;
        var db = b2 - b1;
        var offset = b1 - a1;
        var denominator = Vector2D.Cross(da, db);

        if (Math.Abs(denominator) < Epsilon)
        {
            // Параллельные: пересекаются, только если лежат на одной прямой и перекрываются.
            if (Math.Abs(Vector2D.Cross(offset, da)) > tolerance) return false;
            var lengthSquared = Vector2D.Dot(da, da);
            if (lengthSquared < Epsilon) return false;

            var t0 = Vector2D.Dot(offset, da) / lengthSquared;
            var t1 = t0 + Vector2D.Dot(db, da) / lengthSquared;
            var margin = tolerance / Math.Sqrt(lengthSquared);
            return Math.Max(t0, t1) > margin && Math.Min(t0, t1) < 1 - margin;
        }

        var ta = Vector2D.Cross(offset, db) / denominator;
        var tb = Vector2D.Cross(offset, da) / denominator;
        var marginA = da.Length > Epsilon ? tolerance / da.Length : 0;
        var marginB = db.Length > Epsilon ? tolerance / db.Length : 0;
        return ta > marginA && ta < 1 - marginA && tb > marginB && tb < 1 - marginB;
    }

    /// <summary>Отрезок задевает прямоугольник (отсечение Лианга — Барски).</summary>
    public static bool SegmentHitsBox(Vector2D a, Vector2D b, Box2D box)
    {
        if (box.IsEmpty) return false;
        if (box.Contains(a) || box.Contains(b)) return true;

        double t0 = 0, t1 = 1;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Clip(-dx, a.X - box.MinX, ref t0, ref t1) &&
               Clip(dx, box.MaxX - a.X, ref t0, ref t1) &&
               Clip(-dy, a.Y - box.MinY, ref t0, ref t1) &&
               Clip(dy, box.MaxY - a.Y, ref t0, ref t1) &&
               t0 < t1;
    }

    /// <summary>Отсечение отрезка [a; b] прямоугольником. Возвращает параметры видимой части.</summary>
    public static bool ClipSegment(Vector2D a, Vector2D b, Box2D box, out double t0, out double t1)
    {
        t0 = 0;
        t1 = 1;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Clip(-dx, a.X - box.MinX, ref t0, ref t1) &&
               Clip(dx, box.MaxX - a.X, ref t0, ref t1) &&
               Clip(-dy, a.Y - box.MinY, ref t0, ref t1) &&
               Clip(dy, box.MaxY - a.Y, ref t0, ref t1) &&
               t1 - t0 > 1e-6;
    }

    private static bool Clip(double p, double q, ref double t0, ref double t1)
    {
        if (Math.Abs(p) < 1e-12) return q >= 0;

        var r = q / p;
        if (p < 0)
        {
            if (r > t1) return false;
            if (r > t0) t0 = r;
        }
        else
        {
            if (r < t0) return false;
            if (r < t1) t1 = r;
        }

        return true;
    }

    public static double DistanceToSegment(Vector2D p, Vector2D a, Vector2D b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared;
        if (lengthSquared < Epsilon) return Vector2D.Distance(p, a);

        var t = Math.Max(0, Math.Min(1, Vector2D.Dot(p - a, ab) / lengthSquared));
        return Vector2D.Distance(p, a + ab * t);
    }

    /// <summary>Пересечение выпуклых многоугольников по теореме о разделяющей оси.</summary>
    public static bool ConvexOverlap(IReadOnlyList<Vector2D> a, IReadOnlyList<Vector2D> b)
    {
        return a.Count >= 3 && b.Count >= 3 && !HasSeparatingAxis(a, b) && !HasSeparatingAxis(b, a);
    }

    private static bool HasSeparatingAxis(IReadOnlyList<Vector2D> polygon, IReadOnlyList<Vector2D> other)
    {
        for (var i = 0; i < polygon.Count; i++)
        {
            var axis = (polygon[(i + 1) % polygon.Count] - polygon[i]).Perpendicular();
            if (axis.LengthSquared < Epsilon) continue;

            Project(polygon, axis, out var minA, out var maxA);
            Project(other, axis, out var minB, out var maxB);
            if (maxA <= minB + Epsilon || maxB <= minA + Epsilon) return true;
        }

        return false;
    }

    private static void Project(IReadOnlyList<Vector2D> points, Vector2D axis, out double min, out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;
        foreach (var point in points)
        {
            var value = Vector2D.Dot(point, axis);
            if (value < min) min = value;
            if (value > max) max = value;
        }
    }

    /// <summary>Выпуклая оболочка (монотонная цепочка Эндрю), обход против часовой стрелки.</summary>
    public static List<Vector2D> ConvexHull(IEnumerable<Vector2D> source)
    {
        var points = source
            .OrderBy(p => p.X)
            .ThenBy(p => p.Y)
            .ToList();

        var unique = new List<Vector2D>(points.Count);
        foreach (var point in points)
        {
            if (unique.Count == 0 || !unique[unique.Count - 1].IsNear(point, Epsilon))
                unique.Add(point);
        }

        if (unique.Count < 3) return unique;

        var hull = new List<Vector2D>(unique.Count * 2);
        foreach (var point in unique)
            AppendHullPoint(hull, point, 2);

        var upperMinCount = hull.Count + 1;
        for (var i = unique.Count - 2; i >= 0; i--)
            AppendHullPoint(hull, unique[i], upperMinCount);

        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    private static void AppendHullPoint(List<Vector2D> hull, Vector2D point, int minCount)
    {
        while (hull.Count >= minCount &&
               Vector2D.Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 2]) <= 0)
            hull.RemoveAt(hull.Count - 1);

        hull.Add(point);
    }

    /// <summary>Наклон направления к горизонтали, 0..90°.</summary>
    public static double Inclination(Vector2D direction)
    {
        return Math.Atan2(Math.Abs(direction.Y), Math.Abs(direction.X)) * 180 / Math.PI;
    }
}
