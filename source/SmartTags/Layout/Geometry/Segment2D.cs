namespace SmartTags.Layout.Geometry;

public readonly struct Segment2D(Vector2D start, Vector2D end)
{
    public Vector2D Start { get; } = start;
    public Vector2D End { get; } = end;

    public double Length => Vector2D.Distance(Start, End);
    public Box2D Bounds => new(Start.X, Start.Y, End.X, End.Y);

    public bool Crosses(Segment2D other, double tolerance)
    {
        return Geometry2D.SegmentsCross(Start, End, other.Start, other.End, tolerance);
    }

    public bool Hits(Box2D box) => Geometry2D.SegmentHitsBox(Start, End, box);
}
