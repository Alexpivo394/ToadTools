using System.Text;
using System.Text.RegularExpressions;

namespace ClassifierCode.Engine.Patterns;

/// <summary>Шаблон в профиле не разобрался: битая регулярка или ссылка на неизвестный набор.</summary>
public sealed class PatternException(string message) : Exception(message);

/// <summary>
///     Шаблон не уложился в таймаут. Это ошибка элемента, а не «не совпало»: тихий пропуск под нагрузкой
///     (пауза сборки мусора) дал бы другой код.
/// </summary>
public sealed class PatternTimeoutException(string pattern)
    : Exception($"Шаблон «{pattern}» проверялся слишком долго — упростите его");

/// <summary>
///     Шаблоны профиля → Regex. Простой синтаксис, чтобы обычные правки не требовали регулярок:
///     <c>трап</c> — с начала слова (и <c>трап*</c> так же); <c>*изоляц</c> — где угодно; <c>=пк</c> — слово целиком;
///     <c>шкаф пожарн</c> — слова подряд (у каждого, кроме последнего, допускается окончание);
///     <c>#В1</c> — шифр системы (перед ним не буква, после не цифра), <c>#!В1</c> — перед ним и не цифра;
///     <c>re:…</c> — регулярка как есть; <c>@имя</c> — набор шаблонов профиля.
/// </summary>
public static class PatternCompiler
{
    public const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly IReadOnlyDictionary<string, List<string>> NoSets = new Dictionary<string, List<string>>();

    public static Regex Compile(string pattern, IReadOnlyDictionary<string, List<string>> sets)
    {
        var text = ToRegexText(pattern, sets);
        try
        {
            return new Regex(text, Options, Timeout);
        }
        catch (ArgumentException exception)
        {
            throw new PatternException($"Шаблон «{pattern}» не разобрался: {exception.Message}");
        }
    }

    public static string ToRegexText(string pattern) => ToRegexText(pattern, NoSets);

    public static string ToRegexText(string pattern, IReadOnlyDictionary<string, List<string>> sets) =>
        Translate(pattern, sets, new HashSet<string>());

    private static string Translate(string pattern, IReadOnlyDictionary<string, List<string>> sets, HashSet<string> visiting)
    {
        if (pattern.StartsWith("re:", StringComparison.Ordinal))
            return pattern.Substring(3);

        var trimmed = pattern.Trim();
        if (trimmed.Length == 0)
            throw new PatternException("Пустой шаблон");

        if (trimmed.StartsWith("@", StringComparison.Ordinal))
        {
            var name = trimmed.Substring(1);
            if (!sets.TryGetValue(name, out var items))
                throw new PatternException($"Шаблон «{trimmed}»: нет набора «{name}»");
            if (!visiting.Add(name))
                throw new PatternException($"Шаблон «{trimmed}»: набор ссылается сам на себя");
            var parts = items.Select(item => "(?:" + Translate(item, sets, visiting) + ")");
            visiting.Remove(name);
            return "(?:" + string.Join("|", parts) + ")";
        }

        if (trimmed.StartsWith("#!", StringComparison.Ordinal))
            return @"(?<![\p{L}\d])" + Regex.Escape(trimmed.Substring(2)) + @"(?!\d)";
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
            return @"(?<![\p{L}])" + Regex.Escape(trimmed.Substring(1)) + @"(?!\d)";

        var words = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            var whole = word.StartsWith("=", StringComparison.Ordinal);
            if (whole) word = word.Substring(1);
            var anywhere = word.StartsWith("*", StringComparison.Ordinal);
            if (anywhere) word = word.Substring(1);
            // «трап*» — то же, что «трап»: люди привыкли ставить звёздочку в конце.
            if (word.EndsWith("*", StringComparison.Ordinal)) word = word.Substring(0, word.Length - 1);
            if (word.Length == 0)
                throw new PatternException($"Шаблон «{pattern}»: пустое слово");

            // Следующие слова и так начинаются после пробела — граница слова нужна только перед первым.
            if (i == 0 && (!anywhere || whole)) sb.Append(@"\b");
            sb.Append(Regex.Escape(word));

            var last = i == words.Length - 1;
            if (last)
            {
                if (whole) sb.Append(@"\b");
            }
            else
            {
                sb.Append(whole ? @"\s+" : @"\w*\s+");
            }
        }

        return sb.ToString();
    }
}
