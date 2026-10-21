using System.Globalization;
using System.Text.RegularExpressions;

namespace ParamChecker.Core.Checking;

public static class ValueParser
{
    private static readonly Regex Number = new(@"^[-+]?(\d+(\.\d*)?|\.\d+)([eE][-+]?\d+)?", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Число из текста с запятой или точкой и пробелами между разрядами: «1 200,5» → 1200.5.
    ///     С <paramref name="prefixOnly" /> достаточно, чтобы текст начинался с числа: «1200 мм» → 1200.
    /// </summary>
    public static bool TryParseNumber(string? text, out double value, bool prefixOnly = false)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var compact = new string(text!.Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace(',', '.');
        var match = Number.Match(compact);
        if (!match.Success || (!prefixOnly && match.Length != compact.Length)) return false;

        return double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
