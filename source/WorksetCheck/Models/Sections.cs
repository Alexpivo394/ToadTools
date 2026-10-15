using System.Text.RegularExpressions;

namespace WorksetCheck.Models;

/// <summary>Раздел проекта и варианты его обозначения в именах файлов и рабочих наборов.</summary>
public sealed class Section(string name, params string[] aliases)
{
    /// <summary>Обозначение для новых рабочих наборов и отчёта.</summary>
    public string Name { get; } = name;

    public IReadOnlyList<string> Aliases { get; } = aliases;

    public override string ToString() => Name;
}

/// <summary>
///     Определение раздела по имени. Имя режется на слова (по любым символам кроме букв и цифр),
///     и раздел ищется среди слов целиком: «AR» найдётся в «Дом_AR.rvt» и «Дом_AR2.rvt», но не в «PARKING».
/// </summary>
public static class Sections
{
    /// <summary>Слово, по которому узнаётся РН для CAD-подложек.</summary>
    public const string CadToken = "DWG";

    private static readonly Regex WordRegex = new(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled);

    public static IReadOnlyList<Section> All { get; } =
    [
        new("АР", "АР", "AR", "АИ", "AI"),
        new("КР", "КР", "KR"),
        new("ОВ", "ОВ", "OV", "ОВиК", "OViK", "ОВК", "ОВВ", "ОВО", "OVK", "OVV", "OVO"),
        new("ВК", "ВК", "VK"),
        new("ПТ", "ПТ", "PT"),
        new("СС", "СС", "SS"),
        new("ЭОМ", "ЭОМ", "EOM"),
        new("АВТ", "АВТ", "AVT"),
        new("ИТП", "ИТП", "ITP")
    ];

    /// <summary>Все разделы, обозначения которых встречаются в имени.</summary>
    public static IReadOnlyList<Section> Find(string text)
    {
        var words = Words(text).Select(match => match.Value).ToList();
        return All.Where(section => words.Any(word => section.Aliases.Any(alias => IsAlias(word, alias)))).ToList();
    }

    /// <summary>В имени есть слово «DWG».</summary>
    public static bool HasCadToken(string text) => Words(text).Any(match => IsAlias(match.Value, CadToken));

    /// <summary>Позиция первого слова-раздела (или «DWG») в имени; -1 — такого слова нет.</summary>
    public static int FirstMarkerIndex(string text)
    {
        foreach (var match in Words(text))
        {
            if (IsAlias(match.Value, CadToken) ||
                All.Any(section => section.Aliases.Any(alias => IsAlias(match.Value, alias))))
                return match.Index;
        }

        return -1;
    }

    private static IEnumerable<Match> Words(string text) => WordRegex.Matches(text).Cast<Match>();

    /// <summary>Слово совпадает с обозначением целиком или с номером после него: «ОВ», «ОВ2».</summary>
    private static bool IsAlias(string word, string alias)
    {
        if (!word.StartsWith(alias, StringComparison.OrdinalIgnoreCase)) return false;
        for (var i = alias.Length; i < word.Length; i++)
        {
            if (!char.IsDigit(word[i])) return false;
        }

        return true;
    }
}
