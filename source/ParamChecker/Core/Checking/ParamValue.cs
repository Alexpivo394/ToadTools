namespace ParamChecker.Core.Checking;

/// <summary>Значение параметра элемента так, как его видит проверка.</summary>
/// <param name="Exists">Параметр есть у экземпляра или у типа.</param>
/// <param name="Text">Значение, как его показывает Revit; null — значения нет.</param>
/// <param name="Number">Числовое значение в единицах проекта, если параметр числовой.</param>
public sealed record ParamValue(bool Exists, string? Text, double? Number = null)
{
    public static readonly ParamValue Missing = new(false, null);

    public bool IsFilled => Exists && !string.IsNullOrWhiteSpace(Text);

    public CellStatus Status => !Exists ? CellStatus.Missing : IsFilled ? CellStatus.Filled : CellStatus.Empty;

    /// <summary>Число из значения: числовой параметр как есть, текст — если начинается с числа.</summary>
    public double? AsNumber() => Number ?? (ValueParser.TryParseNumber(Text, out var number, prefixOnly: true) ? number : null);
}

public enum CellStatus
{
    Filled,
    Empty,
    Missing
}
