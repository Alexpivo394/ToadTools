using System.Text.RegularExpressions;
using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Patterns;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Running;

/// <summary>Режим и профиль по имени файла модели.</summary>
public static class ModeDetector
{
    private static readonly Regex Tokens = new(@"[-_.\s()]+", RegexOptions.CultureInvariant);

    /// <summary>Шифр раздела ищется с конца: в "ШФТ-СОБ-1-ПТ" он обычно последний. null — не найден.</summary>
    public static string? DetectMode(CompiledProfile profile, string? documentTitle)
    {
        if (string.IsNullOrEmpty(documentTitle)) return null;
        var tokens = Tokens.Split(TextSourceReader.Normalize(documentTitle));
        for (var i = tokens.Length - 1; i >= 0; i--)
            foreach (var mode in profile.Source.Modes)
                if (mode.FileTokens.Any(t => TextSourceReader.Normalize(t) == tokens[i]))
                    return mode.Key;
        return null;
    }

    /// <summary>Подходит ли имя файла под шаблоны профиля. Битый шаблон — не подходит.</summary>
    public static bool MatchesProfile(ClassifierProfile profile, string? documentTitle)
    {
        if (string.IsNullOrEmpty(documentTitle)) return false;
        foreach (var pattern in profile.FileNamePatterns)
        {
            try
            {
                if (PatternCompiler.Compile(pattern, profile.PatternSets).IsMatch(documentTitle)) return true;
            }
            catch (Exception exception) when (exception is PatternException or RegexMatchTimeoutException)
            {
                // Битый шаблон показывает редактор; при автовыборе он просто не срабатывает.
            }
        }

        return false;
    }
}
