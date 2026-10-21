namespace ParamChecker.Core.Checking;

/// <summary>Итог проверки одной модели по всем правилам профиля.</summary>
public sealed class ModelReport
{
    public string ProfileName { get; init; } = "";
    public string ModelName { get; init; } = "";
    public string SourcePath { get; init; } = "";

    /// <summary>Вид, с которого собраны элементы; пусто — вся модель.</summary>
    public string ViewName { get; init; } = "";

    public DateTime CheckedAt { get; init; }
    public List<RuleReport> Rules { get; } = [];

    public int Elements => Rules.Sum(rule => rule.Elements);
    public int CompleteElements => Rules.Sum(rule => rule.CompleteElements);
    public int Values => Rules.Sum(rule => rule.Values);
    public int Filled => Rules.Sum(rule => rule.Filled);
    public int Empty => Rules.Sum(rule => rule.Empty);
    public int Missing => Rules.Sum(rule => rule.Missing);

    /// <summary>Доля заполненных значений среди всех проверенных: элементы × параметры.</summary>
    public double? FillRate => Ratio(Filled, Values);

    /// <summary>Доля элементов, у которых заполнено всё, что проверялось.</summary>
    public double? Completeness => Ratio(CompleteElements, Elements);

    internal static double? Ratio(int part, int total) => total == 0 ? null : (double)part / total;
}

/// <summary>Итог одного правила: статистика по параметрам и строки элементов для листа отчёта.</summary>
public sealed class RuleReport
{
    public RuleReport(string title, IReadOnlyList<string> parameters)
    {
        Title = title;
        Parameters = parameters;
        Stats = parameters.Select(name => new ParameterStats(name)).ToList();
    }

    public string Title { get; }
    public IReadOnlyList<string> Parameters { get; }
    public IReadOnlyList<ParameterStats> Stats { get; }

    /// <summary>Строки отчёта. С настройкой «только проблемные» сюда попадают не все проверенные элементы.</summary>
    public List<ElementRow> Rows { get; } = [];

    public int Elements { get; private set; }
    public int CompleteElements { get; private set; }

    public int Values => Stats.Sum(s => s.Total);
    public int Filled => Stats.Sum(s => s.Filled);
    public int Empty => Stats.Sum(s => s.Empty);
    public int Missing => Stats.Sum(s => s.Missing);
    public double? FillRate => ModelReport.Ratio(Filled, Values);
    public double? Completeness => ModelReport.Ratio(CompleteElements, Elements);

    /// <summary>Учитывает элемент в статистике; строку в отчёт добавляет вызывающий.</summary>
    /// <returns>У элемента заполнены все параметры.</returns>
    public bool Count(IReadOnlyList<ParamValue> values)
    {
        Elements++;
        var complete = true;
        for (var i = 0; i < values.Count; i++)
        {
            var status = values[i].Status;
            Stats[i].Add(status);
            complete &= status == CellStatus.Filled;
        }

        if (complete) CompleteElements++;
        return complete;
    }
}

public sealed class ParameterStats(string name)
{
    public string Name { get; } = name;
    public int Filled { get; private set; }
    public int Empty { get; private set; }
    public int Missing { get; private set; }
    public int Total => Filled + Empty + Missing;
    public double? FillRate => ModelReport.Ratio(Filled, Total);

    internal void Add(CellStatus status)
    {
        switch (status)
        {
            case CellStatus.Filled: Filled++; break;
            case CellStatus.Empty: Empty++; break;
            default: Missing++; break;
        }
    }
}

/// <summary>Элемент в листе правила.</summary>
public sealed record ElementRow(long Id, string Category, string Family, string Type, string Workset, IReadOnlyList<ParamValue> Values);
