namespace ParamChecker.ViewModels;

internal static class Words
{
    /// <summary>«1 модель», «2 модели», «5 моделей».</summary>
    public static string Count(int count, string one, string few, string many) => $"{count} {Plural(count, one, few, many)}";

    public static string Plural(int count, string one, string few, string many) => (count % 100, count % 10) switch
    {
        ( >= 11 and <= 14, _) => many,
        (_, 1) => one,
        (_, >= 2 and <= 4) => few,
        _ => many
    };

    /// <summary>Первые элементы списка через запятую и «и ещё N».</summary>
    public static string List(IReadOnlyList<string> items, int shown)
    {
        var text = string.Join(", ", items.Take(shown));
        return items.Count > shown ? $"{text} и ещё {items.Count - shown}" : text;
    }
}
