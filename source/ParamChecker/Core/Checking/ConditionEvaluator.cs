using ParamChecker.Core.Profiles;

namespace ParamChecker.Core.Checking;

/// <summary>Отбор элемента правилом по категории и условиям на параметры. Revit сюда не попадает — только значения.</summary>
public static class ConditionEvaluator
{
    private const double Tolerance = 1e-6;

    /// <param name="filter">Отбор правила.</param>
    /// <param name="categoryMatches">Категория элемента среди выбранных.</param>
    /// <param name="read">Значение параметра элемента по имени.</param>
    public static bool Accepts(RuleFilter filter, bool categoryMatches, Func<string, ParamValue> read) => filter.Mode switch
    {
        SelectionMode.CategoriesOnly => categoryMatches,
        SelectionMode.ParametersOnly => Matches(filter, read),
        SelectionMode.CategoriesOrParameters => categoryMatches || (HasConditions(filter) && Matches(filter, read)),
        _ => categoryMatches && Matches(filter, read)
    };

    /// <summary>Условия верхнего уровня, связанные логикой фильтра. Без условий подходит любой элемент.</summary>
    public static bool Matches(RuleFilter filter, Func<string, ParamValue> read)
    {
        var conditions = filter.Conditions.Where(IsComplete).ToList();
        if (conditions.Count == 0) return true;

        return filter.Logic == ConditionLogic.Or
            ? conditions.Any(condition => Matches(condition, read))
            : conditions.All(condition => Matches(condition, read));
    }

    /// <summary>Условие или группа условий; группа — все её условия через «И».</summary>
    public static bool Matches(Condition condition, Func<string, ParamValue> read)
    {
        if (condition.IsGroup) return condition.Group!.Where(IsComplete).All(child => Matches(child, read));

        var value = read(condition.Parameter!.Trim());
        var expected = (condition.Value ?? "").Trim();
        var text = value.Text?.Trim();

        return condition.Operator switch
        {
            ConditionOperator.Equals => AreEqual(value, expected),
            ConditionOperator.NotEquals => !AreEqual(value, expected),
            ConditionOperator.GreaterThan => Compare(value, expected) is > 0,
            ConditionOperator.GreaterThanOrEquals => Compare(value, expected) is >= 0,
            ConditionOperator.LessThan => Compare(value, expected) is < 0,
            ConditionOperator.LessThanOrEquals => Compare(value, expected) is <= 0,
            ConditionOperator.Contains => text is not null && text.IndexOf(expected, StringComparison.CurrentCultureIgnoreCase) >= 0,
            ConditionOperator.NotContains => text is null || text.IndexOf(expected, StringComparison.CurrentCultureIgnoreCase) < 0,
            ConditionOperator.StartsWith => text is not null && text.StartsWith(expected, StringComparison.CurrentCultureIgnoreCase),
            ConditionOperator.EndsWith => text is not null && text.EndsWith(expected, StringComparison.CurrentCultureIgnoreCase),
            ConditionOperator.Exists => value.Exists,
            ConditionOperator.NotExists => !value.Exists,
            ConditionOperator.IsFilled => value.IsFilled,
            ConditionOperator.IsEmpty => !value.IsFilled,
            _ => false
        };
    }

    /// <summary>
    ///     В фильтре есть хоть одно дописанное условие. Без условий «категории ИЛИ условия» — это только категории:
    ///     иначе пустой список условий пропускал бы все элементы модели.
    /// </summary>
    public static bool HasConditions(RuleFilter filter) => filter.Conditions.Any(IsComplete);

    /// <summary>Оператору не нужно значение — поле значения можно спрятать.</summary>
    public static bool NeedsValue(ConditionOperator op) =>
        op is not (ConditionOperator.Exists or ConditionOperator.NotExists or ConditionOperator.IsFilled or ConditionOperator.IsEmpty);

    /// <summary>Недописанное условие (без параметра или пустая группа) не учитывается.</summary>
    public static bool IsComplete(Condition condition) =>
        condition.IsGroup ? condition.Group!.Any(IsComplete) : !string.IsNullOrWhiteSpace(condition.Parameter) && condition.Operator is not null;

    /// <summary>Равенство: если ожидается число, а параметр числовой, — сравнение чисел («1200» = «1200 мм»), иначе текста.</summary>
    private static bool AreEqual(ParamValue value, string expected)
    {
        if (!value.Exists) return false;

        if (ValueParser.TryParseNumber(expected, out var number) && value.AsNumber() is { } actual)
            return Math.Abs(actual - number) <= Tolerance * Math.Max(1, Math.Abs(number));

        return string.Equals(value.Text?.Trim() ?? "", expected, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Сравнение чисел; null — одно из значений не число, и условие не выполняется.</summary>
    private static int? Compare(ParamValue value, string expected)
    {
        if (!value.Exists || value.AsNumber() is not { } actual || !ValueParser.TryParseNumber(expected, out var number))
            return null;

        return Math.Abs(actual - number) <= Tolerance * Math.Max(1, Math.Abs(number)) ? 0 : actual.CompareTo(number);
    }
}
