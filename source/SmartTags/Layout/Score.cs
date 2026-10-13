using System.Globalization;

namespace SmartTags.Layout;

/// <summary>
///     Оценка положения: число жёстких конфликтов (пересечения) и сумма мягких штрафов (некрасиво).
///     Сравнение лексикографическое — сначала жёсткие, потом мягкие.
/// </summary>
public readonly struct Score(int hard, double soft)
{
    public static readonly Score Zero = new(0, 0);
    public static readonly Score Worst = new(int.MaxValue, double.MaxValue);

    public int Hard { get; } = hard;
    public double Soft { get; } = soft;

    public static Score operator +(Score a, Score b) => new(a.Hard + b.Hard, a.Soft + b.Soft);
    public static Score operator -(Score a, Score b) => new(a.Hard - b.Hard, a.Soft - b.Soft);

    public bool IsBetterThan(Score other)
    {
        return Hard != other.Hard ? Hard < other.Hard : Soft < other.Soft - 1e-9;
    }

    /// <summary>Идеал: конфликтов нет, бонусы за выравнивание перекрыли штрафы.</summary>
    public bool IsPerfect => Hard == 0 && Soft <= 0;

    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "жёстких {0}, мягких {1:0.###}", Hard, Soft);
    }
}
