namespace SmartTags.Layout;

public enum LayoutQuality
{
    Fast,
    Standard,
    Maximum
}

/// <summary>С какой стороны от элемента ставить марку (на листе).</summary>
public enum TagSide
{
    Auto,
    Top,
    Bottom,
    Left,
    Right
}

/// <summary>Параметры раскладки. Все размеры — в мм на листе.</summary>
public sealed class LayoutSettings
{
    /// <summary>Минимальный зазор вокруг текста марки (меньше — жёсткий конфликт).</summary>
    public double MinGap { get; set; } = 1.5;

    /// <summary>Комфортный зазор (меньше — штраф «слишком близко»).</summary>
    public double SoftGap { get; set; } = 3;

    public double MinLeader { get; set; } = 5;
    public double PreferredLeader { get; set; } = 12;
    public double MaxLeader { get; set; } = 35;

    /// <summary>Длина горизонтальной полки между изломом выноски и текстом.</summary>
    public double Shelf { get; set; } = 1.5;

    public double MinShelf { get; set; } = 0.5;
    public double MaxShelf { get; set; } = 4;

    /// <summary>Предпочтительные углы наклона выноски к горизонтали.</summary>
    public List<double> PreferredAngles { get; set; } = [30, 45, 60];

    /// <summary>Текст поверх инженерных элементов — жёсткий конфликт (иначе только штраф).</summary>
    public bool AvoidTextOverElements { get; set; } = true;

    /// <summary>
    ///     Элемент тоньше этого (линия схемы, тонкая труба) текст может перекрыть со штрафом: подпись остаётся
    ///     читаемой, а на плотной схеме иначе марку не поставить рядом с элементом.
    /// </summary>
    public double ThinElement { get; set; } = 1.5;

    /// <summary>Какую долю текста может закрыть элемент толще <see cref="ThinElement" /> (больше — жёсткий конфликт).</summary>
    public double MaxTextCover { get; set; } = 0.15;

    /// <summary>
    ///     Сторона, куда уходит выноска от элемента. Не та сторона — крупный мягкий штраф: марка уходит
    ///     на другую сторону, только если на заданной у неё остаются жёсткие конфликты.
    /// </summary>
    public TagSide Side { get; set; } = TagSide.Auto;

    public LayoutQuality Quality { get; set; } = LayoutQuality.Standard;
    public int Seed { get; set; } = 20240917;
    public int MaxCandidatesPerItem { get; set; } = 160;

    /// <summary>На сколько можно сдвинуть текст ради выравнивания с соседями.</summary>
    public double AlignSnap { get; set; } = 6;

    /// <summary>Марки элементов, чьи точки привязки ближе этого расстояния, выстраиваются в общий столбик/строку.</summary>
    public double ArrangeRadius { get; set; } = 8;

    /// <summary>На сколько (мягких штрафов на марку) упорядоченная группа может быть «хуже» разбросанной.</summary>
    public double ArrangeTolerance { get; set; } = 1.5;

    // Веса мягких штрафов.
    public double WeightLongLeader { get; set; } = 1;
    public double WeightShortLeader { get; set; } = 0.6;
    public double WeightAnglePerDegree { get; set; } = 0.04;
    public double WeightLeaderOverElement { get; set; } = 2;
    public double WeightTextOverElement { get; set; } = 6;
    public double WeightDensity { get; set; } = 0.35;
    public double WeightAlignment { get; set; } = 1.2;
    public double WeightSideMismatch { get; set; } = 0.6;
    public double WeightSharedAnchor { get; set; } = 2;
    public double WeightTooClose { get; set; } = 1;
    public double WeightAnchorRank { get; set; } = 0.15;
    public double WeightReversedShelf { get; set; } = 1.5;
    public double WeightFlatLeader { get; set; } = 1.5;
    public double WeightAlongAxis { get; set; } = 6;
    public double WeightParallel { get; set; } = 0.4;
    public double WeightWrongSide { get; set; } = 50;

    /// <summary>Сколько точек привязки перебирать у элемента.</summary>
    public int AnchorSamples => Quality switch 
    {
        LayoutQuality.Fast => 3,
        LayoutQuality.Maximum => 7,
        _ => 5
    };

    /// <summary>Шаг перебора направлений выноски, градусы.</summary>
    public double DirectionStep => Quality switch
    { 
        LayoutQuality.Fast => 45,
        LayoutQuality.Maximum => 15,
        _ => 30
    };

    /// <summary>Бюджет времени на оптимизацию: растёт с числом марок и качеством.</summary>
    public int TimeBudgetMs(int itemCount)
    {
        var n = Math.Max(1, itemCount);
        var budget = n <= 300 ? 400 + 20.0 * n : 6000 + 20.0 * (n - 300);
        var factor = Quality switch
        {
            LayoutQuality.Fast => 0.4,
            LayoutQuality.Maximum => 3.0,
            _ => 1.0
        };

        return (int)Math.Max(200, Math.Min(90_000, budget * factor));
    }
}
