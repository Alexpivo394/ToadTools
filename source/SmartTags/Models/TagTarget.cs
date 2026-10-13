using SmartTags.Layout.Geometry;

namespace SmartTags.Models;

/// <summary>
///     Что маркируется одной маркой: отдельный элемент или участок из нескольких труб/воздуховодов
///     одного размера и системы, идущих по одной прямой.
/// </summary>
public sealed class TagTarget(Element primary, bool isRun)
{
    public List<Element> Elements { get; } = [primary];

    /// <summary>Элемент, на который ставится марка (у участка — самый длинный видимый кусок).</summary>
    public Element Primary { get; set; } = primary;

    public bool IsRun { get; } = isRun;
    public string Label { get; set; } = string.Empty;
    public CategoryChoice Choice { get; set; } = null!;

    /// <summary>Направление участка в модели (у штучных элементов — null).</summary>
    public XYZ? Axis { get; set; }

    public double LengthMm { get; set; }

    /// <summary>Точки, куда может упираться выноска. Первая — предпочтительная.</summary>
    public List<XYZ> Anchors { get; } = [];

    /// <summary>Глубина точек привязки вдоль направления взгляда — на ней ставится голова марки.</summary>
    public double Depth { get; set; }

    public SkipReason Skip { get; private set; }
    public string? SkipDetails { get; private set; }
    public bool IsSkipped => Skip != SkipReason.None;

    /// <summary>Марка, уже стоявшая на элементе (режим «переразместить»).</summary>
    public IndependentTag? ExistingTag { get; set; }

    public IndependentTag? Tag { get; set; }

    /// <summary>Размер текста марки и смещение его угла от головы марки, мм на листе.</summary>
    public double TextWidth { get; set; }

    public double TextHeight { get; set; }
    public Vector2D TextOffset { get; set; }
    public bool Measured { get; set; }

    /// <summary>Индекс в задаче раскладки (-1 — в раскладку не попал).</summary>
    public int LayoutIndex { get; set; } = -1;

    public IEnumerable<ElementId> ElementIds => Elements.Select(element => element.Id);

    /// <summary>Запоминает первую причину пропуска, последующие игнорирует.</summary>
    public void MarkSkipped(SkipReason reason, string? details = null)
    {
        if (IsSkipped) return;

        Skip = reason;
        SkipDetails = details;
    }
}
