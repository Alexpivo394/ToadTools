namespace SmartTags.Layout;

public enum LayoutQuality
{
    Fast,
    Standard,
    Maximum
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
    public double Shelf { get; set; } = 5;

    public double MinShelf { get; set; } = 3;
    public double MaxShelf { get; set; } = 8;

    /// <summary>Предпочтительные углы наклона выноски к горизонтали.</summary>
    public List<double> PreferredAngles { get; set; } = [30, 45, 60];

    /// <summary>Текст поверх инженерных элементов — жёсткий конфликт (иначе только штраф).</summary>
    public bool AvoidTextOverElements { get; set; } = true;

    public LayoutQuality Quality { get; set; } = LayoutQuality.Standard;
    public int Seed { get; set; } = 20240917;
    public int MaxCandidatesPerItem { get; set; } = 160;

    /// <summary>На сколько можно сдвинуть текст ради выравнивания с соседями.</summary>
    public double AlignSnap { get; set; } = 6;

    // Веса мягких штрафов.
    public double WeightLongLeader { get; set; } = 1;
    public double WeightShortLeader { get; set; } = 0.6;
    public double WeightAnglePerDegree { get; set; } = 0.02;
    public double WeightLeaderOverElement { get; set; } = 3;
    public double WeightTextOverElement { get; set; } = 6;
    public double WeightDensity { get; set; } = 0.35;
    public double WeightAlignment { get; set; } = 1.2;
    public double WeightSideMismatch { get; set; } = 0.6;
    public double WeightSharedAnchor { get; set; } = 2;
    public double WeightTooClose { get; set; } = 1;
    public double WeightAnchorRank { get; set; } = 0.15;
    public double WeightReversedShelf { get; set; } = 0.4;

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
