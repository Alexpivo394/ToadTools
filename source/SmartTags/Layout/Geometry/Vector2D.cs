using System.Globalization;

namespace SmartTags.Layout.Geometry;

/// <summary>Точка/вектор в плоскости вида. Единицы — мм на листе.</summary>
public readonly struct Vector2D(double x, double y)
{
    public static readonly Vector2D Zero = new(0, 0);
    public static readonly Vector2D UnitX = new(1, 0);

    public double X { get; } = x;
    public double Y { get; } = y;

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public static Vector2D operator +(Vector2D a, Vector2D b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2D operator -(Vector2D a, Vector2D b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2D operator -(Vector2D a) => new(-a.X, -a.Y);
    public static Vector2D operator *(Vector2D a, double k) => new(a.X * k, a.Y * k);

    public static double Dot(Vector2D a, Vector2D b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(Vector2D a, Vector2D b) => a.X * b.Y - a.Y * b.X;
    public static double Distance(Vector2D a, Vector2D b) => (a - b).Length;
    public static Vector2D Lerp(Vector2D a, Vector2D b, double t) => a + (b - a) * t;

    public Vector2D Normalize()
    {
        var length = Length;
        return length < 1e-12 ? Zero : new Vector2D(X / length, Y / length);
    }

    /// <summary>Поворот на 90° против часовой стрелки.</summary>
    public Vector2D Perpendicular() => new(-Y, X);

    public Vector2D Rotate(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new Vector2D(X * cos - Y * sin, X * sin + Y * cos);
    }

    public static Vector2D FromAngle(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Vector2D(Math.Cos(radians), Math.Sin(radians));
    }

    public bool IsNear(Vector2D other, double tolerance)
    {
        return Math.Abs(X - other.X) <= tolerance && Math.Abs(Y - other.Y) <= tolerance;
    }

    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "({0:0.##}; {1:0.##})", X, Y);
    }
}
