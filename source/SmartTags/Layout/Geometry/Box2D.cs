using System.Globalization;

namespace SmartTags.Layout.Geometry;

/// <summary>Прямоугольник, выровненный по осям вида.</summary>
public readonly struct Box2D
{
    public Box2D(double minX, double minY, double maxX, double maxY)
    {
        MinX = Math.Min(minX, maxX);
        MaxX = Math.Max(minX, maxX);
        MinY = Math.Min(minY, maxY);
        MaxY = Math.Max(minY, maxY);
    }

    private Box2D(bool empty)
    {
        MinX = MinY = double.PositiveInfinity;
        MaxX = MaxY = double.NegativeInfinity;
    }

    public static Box2D Empty { get; } = new(true);

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }

    public bool IsEmpty => double.IsInfinity(MinX) || MaxX < MinX || MaxY < MinY;
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public Vector2D Min => new(MinX, MinY);
    public Vector2D Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);
    public Vector2D LeftMiddle => new(MinX, (MinY + MaxY) / 2);
    public Vector2D RightMiddle => new(MaxX, (MinY + MaxY) / 2);

    public static Box2D FromCenter(Vector2D center, double width, double height)
    {
        return new Box2D(center.X - width / 2, center.Y - height / 2, center.X + width / 2, center.Y + height / 2);
    }

    public static Box2D FromMin(Vector2D min, double width, double height)
    {
        return new Box2D(min.X, min.Y, min.X + width, min.Y + height);
    }

    public Box2D Inflate(double amount)
    {
        return IsEmpty ? this : new Box2D(MinX - amount, MinY - amount, MaxX + amount, MaxY + amount);
    }

    public Box2D Shift(double dx, double dy) => new(MinX + dx, MinY + dy, MaxX + dx, MaxY + dy);

    public bool Contains(Vector2D p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    public bool Contains(Box2D other)
    {
        return other.MinX >= MinX && other.MaxX <= MaxX && other.MinY >= MinY && other.MaxY <= MaxY;
    }

    /// <summary>Пересечение с ненулевой площадью: касание по границе не считается.</summary>
    public bool Overlaps(Box2D other)
    {
        return other.MinX < MaxX && other.MaxX > MinX && other.MinY < MaxY && other.MaxY > MinY;
    }

    public Box2D Union(Box2D other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return new Box2D(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
            Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));
    }

    public Box2D Union(Vector2D p)
    {
        if (IsEmpty) return new Box2D(p.X, p.Y, p.X, p.Y);
        return new Box2D(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));
    }

    /// <summary>Зазор по X между коробками (0, если проекции перекрываются).</summary>
    public double GapX(Box2D other) => Math.Max(0, Math.Max(other.MinX - MaxX, MinX - other.MaxX));

    /// <summary>Зазор по Y между коробками (0, если проекции перекрываются).</summary>
    public double GapY(Box2D other) => Math.Max(0, Math.Max(other.MinY - MaxY, MinY - other.MaxY));

    public double DistanceTo(Box2D other)
    {
        var dx = GapX(other);
        var dy = GapY(other);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public Vector2D[] Corners()
    {
        return [new(MinX, MinY), new(MaxX, MinY), new(MaxX, MaxY), new(MinX, MaxY)];
    }

    public override string ToString()
    {
        return IsEmpty
            ? "[пусто]"
            : string.Format(CultureInfo.InvariantCulture, "[{0:0.#};{1:0.#} - {2:0.#};{3:0.#}]", MinX, MinY, MaxX, MaxY);
    }
}
