using SmartTags.Layout.Geometry;

namespace SmartTags.Layout;

/// <summary>С какой стороны от излома выноски стоит текст.</summary>
public enum ShelfSide
{
    Right,
    Left
}

/// <summary>
///     Положение марки: выноска «точка привязки → излом», горизонтальная полка «излом → текст»
///     и прямоугольник текста, по вертикали отцентрованный на полке.
/// </summary>
public sealed class Placement
{
    private Placement(Vector2D anchor, Vector2D elbow, Vector2D attach, Box2D text, ShelfSide side, int anchorIndex)
    {
        Anchor = anchor;
        Elbow = elbow;
        Attach = attach;
        Text = text;
        Side = side;
        AnchorIndex = anchorIndex;

        var leader = elbow - anchor;
        LeaderLength = leader.Length;
        Direction = leader.Normalize();
        Bounds = text.Union(anchor).Union(elbow);
    }

    public Vector2D Anchor { get; }
    public Vector2D Elbow { get; }

    /// <summary>Точка, где полка упирается в текст.</summary>
    public Vector2D Attach { get; }

    public Box2D Text { get; }
    public ShelfSide Side { get; }
    public int AnchorIndex { get; }
    public double LeaderLength { get; }
    public Vector2D Direction { get; }
    public Box2D Bounds { get; }
    public double ShelfLength => Math.Abs(Attach.X - Elbow.X);

    /// <summary>Марка входит в «столбик», собранный для разрешения плотного скопления.</summary>
    public bool Stacked { get; set; }

    /// <summary>X кромки текста со стороны полки — по ней выравниваются соседние марки.</summary>
    public double AlignEdge => Side == ShelfSide.Right ? Text.MinX : Text.MaxX;

    public Segment2D Leader => new(Anchor, Elbow);
    public Segment2D Shelf => new(Elbow, Attach);

    /// <summary>Оценка без учёта соседних марок — кэшируется, т. к. не меняется за время раскладки.</summary>
    internal Score? OwnScore { get; set; }

    /// <summary>Строит положение от точки привязки: направление и длина выноски, сторона и длина полки.</summary>
    public static Placement FromLeader(LayoutItem item, int anchorIndex, Vector2D direction, double length,
        ShelfSide side, double shelf)
    {
        var anchor = item.Anchors[anchorIndex];
        var elbow = anchor + direction.Normalize() * length;
        var attach = new Vector2D(side == ShelfSide.Right ? elbow.X + shelf : elbow.X - shelf, elbow.Y);
        var text = TextAt(item, attach, side);
        return new Placement(anchor, elbow, attach, text, side, anchorIndex);
    }

    /// <summary>Строит положение от заданного прямоугольника текста: выноска тянется к нему от точки привязки.</summary>
    public static Placement FromText(LayoutItem item, int anchorIndex, Box2D text, ShelfSide side, double shelf)
    {
        var anchor = item.Anchors[anchorIndex];
        var y = text.Center.Y;
        var attach = side == ShelfSide.Right ? new Vector2D(text.MinX, y) : new Vector2D(text.MaxX, y);
        var elbow = new Vector2D(side == ShelfSide.Right ? attach.X - shelf : attach.X + shelf, y);
        return new Placement(anchor, elbow, attach, text, side, anchorIndex);
    }

    private static Box2D TextAt(LayoutItem item, Vector2D attach, ShelfSide side)
    {
        var minX = side == ShelfSide.Right ? attach.X : attach.X - item.TextWidth;
        return Box2D.FromMin(new Vector2D(minX, attach.Y - item.TextHeight / 2), item.TextWidth, item.TextHeight);
    }
}
