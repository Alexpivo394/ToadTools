using System.Text.RegularExpressions;

namespace ClassifierCode.Engine.Patterns;

/// <summary>
///     Сопоставление с защитой от «взрывных» шаблонов. Таймаут — по часам, поэтому случайная пауза (сборка мусора
///     на большой модели) может его сработать: тогда повторяем один раз. Не уложился дважды — шаблон сломан, и до
///     конца сессии Revit (по тексту шаблона, а не по объекту — пересборка профиля в редакторе его не «лечит»)
///     следующие элементы сразу получают ошибку без ожидания. Исправленный шаблон — это уже другой текст.
/// </summary>
public static class SafeMatch
{
    private static readonly HashSet<string> Broken = [];

    public static Match Match(Regex regex, string pattern, string text) => Run(regex, pattern, () => regex.Match(text));

    public static bool IsMatch(Regex regex, string pattern, string text) => Run(regex, pattern, () => regex.IsMatch(text));

    private static string Key(Regex regex) => (int)regex.Options + "\n" + regex;

    private static T Run<T>(Regex regex, string pattern, Func<T> match)
    {
        var key = Key(regex);
        lock (Broken)
        {
            if (Broken.Contains(key)) throw new PatternTimeoutException(pattern);
        }

        for (var attempt = 0;; attempt++)
        {
            try
            {
                return match();
            }
            catch (RegexMatchTimeoutException) when (attempt == 0)
            {
                // Возможно, просто пауза процесса — пробуем ещё раз.
            }
            catch (RegexMatchTimeoutException)
            {
                lock (Broken) Broken.Add(key);
                throw new PatternTimeoutException(pattern);
            }
        }
    }
}
