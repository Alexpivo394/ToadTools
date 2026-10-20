using SmartTags.Layout.Geometry;

namespace SmartTags.Layout;

/// <summary>Марка, которую нужно разместить: точки привязки и размер текста.</summary>
public sealed class LayoutItem
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;

    /// <summary>Точки, куда может упираться выноска. Первая — предпочтительная.</summary>
    public List<Vector2D> Anchors { get; } = [];

    /// <summary>
    ///     Общая подпись: точки других элементов с такой же маркой рядом. Их выноски сходятся к излому
    ///     этой марки, а текст ставится в то же место — на чертеже это одна подпись с несколькими выносками.
    /// </summary>
    public List<Vector2D> ExtraAnchors { get; } = [];

    /// <summary>Направление оси для линейных элементов (труба, воздуховод), иначе ноль.</summary>
    public Vector2D Axis { get; set; }

    public double TextWidth { get; set; }
    public double TextHeight { get; set; }

    /// <summary>
    ///     Высота над низом текста, на которой Revit подводит выноску к марке (середина рамки марки).
    ///     У марок с подчёркиванием это уровень подчёркивания, а не середина текста. Null — середина текста.
    /// </summary>
    public double? AttachHeight { get; set; }

    /// <summary>Высота полки (точки крепления выноски) над низом текста.</summary>
    public double ShelfHeight => AttachHeight ?? TextHeight / 2;

    /// <summary>Контур своего элемента: выноска может по нему идти без штрафа.</summary>
    public ConvexPolygon2D? Footprint { get; set; }

    /// <summary>Чем больше, тем раньше ставится при жадной раскладке.</summary>
    public int Priority { get; set; }

    /// <summary>Марки одной группы рядом стараются ставить по одну сторону (0 — без группы).</summary>
    public int Group { get; set; }

    public bool IsLinear => Axis.LengthSquared > 0.5;
    public Vector2D MainAnchor => Anchors.Count > 0 ? Anchors[0] : Vector2D.Zero;
}

public enum ObstacleKind
{
    /// <summary>Инженерный элемент на виде.</summary>
    Element,

    /// <summary>Текст уже стоящей на виде марки.</summary>
    TagText,

    /// <summary>Выноска уже стоящей на виде марки.</summary>
    TagLeader,

    /// <summary>Текстовые примечания, марки помещений и прочие аннотации.</summary>
    Annotation
}

/// <summary>Неподвижное препятствие: прямоугольник, выпуклый многоугольник или отрезок.</summary>
public sealed class LayoutObstacle
{
    private LayoutObstacle(ObstacleKind kind, string label)
    {
        Kind = kind;
        Label = label;
    }

    public int Id { get; set; }
    public ObstacleKind Kind { get; }
    public string Label { get; }

    /// <summary>Марка, чьему элементу принадлежит препятствие (-1 — ничьё).</summary>
    public int OwnerItem { get; set; } = -1;

    public Box2D? Box { get; private init; }
    public ConvexPolygon2D? Polygon { get; private init; }
    public Segment2D? Segment { get; private init; }

    public Box2D Bounds => Box ?? Polygon?.Bounds ?? Segment?.Bounds ?? Box2D.Empty;

    /// <summary>Толщина на листе: у трубы или воздуховода — ширина полосы, у оборудования — меньший размер.</summary>
    public double Thickness => _thickness ??= Box is { } box ? Math.Min(box.Width, box.Height) : Polygon?.Thickness ?? 0;

    private double? _thickness;

    /// <summary>Какую долю прямоугольника закрывает препятствие (0…1).</summary>
    public double Covers(Box2D box)
    {
        var area = box.Width * box.Height;
        if (area <= 1e-9) return 0;

        var common = Box is { } own
            ? Math.Max(0, Math.Min(own.MaxX, box.MaxX) - Math.Max(own.MinX, box.MinX)) *
              Math.Max(0, Math.Min(own.MaxY, box.MaxY) - Math.Max(own.MinY, box.MinY))
            : Polygon?.IntersectionArea(box) ?? 0;
        return Math.Min(1, common / area);
    }

    public static LayoutObstacle FromBox(ObstacleKind kind, Box2D box, string label) => new(kind, label) { Box = box };

    public static LayoutObstacle FromPolygon(ObstacleKind kind, ConvexPolygon2D polygon, string label)
    {
        return new LayoutObstacle(kind, label) { Polygon = polygon };
    }

    public static LayoutObstacle FromSegment(ObstacleKind kind, Segment2D segment, string label)
    {
        return new LayoutObstacle(kind, label) { Segment = segment };
    }

    public bool Overlaps(Box2D box)
    {
        if (Box is { } own) return own.Overlaps(box);
        if (Polygon is not null) return Polygon.Overlaps(box);
        return Segment is { } segment && segment.Hits(box);
    }

    public bool Hits(Segment2D segment, double tolerance)
    {
        if (Box is { } own) return segment.Hits(own);
        if (Polygon is not null) return Polygon.Hits(segment, tolerance);
        return Segment is { } other && other.Crosses(segment, tolerance);
    }
}

/// <summary>Исходные данные для раскладки.</summary>
public sealed class LayoutRequest
{
    public List<LayoutItem> Items { get; } = [];
    public List<LayoutObstacle> Obstacles { get; } = [];
    public LayoutSettings Settings { get; set; } = new();

    /// <summary>Область, за которую текст выходить не должен (подрезка вида). Null — без ограничения.</summary>
    public Box2D? Bounds { get; set; }

    /// <summary>Непрямоугольная подрезка вида, уточняет <see cref="Bounds" />.</summary>
    public ConvexPolygon2D? BoundsShape { get; set; }
}
