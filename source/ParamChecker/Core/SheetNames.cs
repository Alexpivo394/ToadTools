namespace ParamChecker.Core;

/// <summary>Имена листов Excel: не длиннее 31 символа, без <c>[]:*?/\</c>, без повторов (регистр не важен).</summary>
public sealed class SheetNames
{
    private const int MaxLength = 31;
    private static readonly char[] Invalid = ['[', ']', ':', '*', '?', '/', '\\'];

    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="reserved">Имена служебных листов, которые правилам занимать нельзя.</param>
    public SheetNames(params string[] reserved)
    {
        foreach (var name in reserved) _used.Add(name);
    }

    /// <summary>Свободное имя листа из названия правила; пустое название — «Правило N».</summary>
    public string Next(string title, int index)
    {
        var name = new string((title ?? "").Select(c => Invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().Trim('\'');
        if (name.Length == 0) name = $"Правило {index}";
        if (name.Length > MaxLength) name = name.Substring(0, MaxLength).TrimEnd();

        var candidate = name;
        for (var n = 2; !_used.Add(candidate); n++)
        {
            var suffix = $" ({n})";
            candidate = (name.Length + suffix.Length > MaxLength ? name.Substring(0, MaxLength - suffix.Length) : name) + suffix;
        }

        return candidate;
    }
}
