using System.Globalization;
using System.Text.RegularExpressions;

namespace Collisions.Core;

/// <summary>
///     Коллизия из вставленного текста: ссылка ToadFlow с <c>collision={id}</c>, «ToadClash №123» из «Показать в Revit» или
///     просто номер.
/// </summary>
public sealed record ClashLink(Guid? CollisionId, int? Number)
{
    private static readonly Regex Link = new(@"[?&]collision=([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.CultureInvariant);

    private static readonly Regex Text = new(@"ToadClash\s*№\s*(\d{1,9})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Digits = new(@"^\d{1,9}$", RegexOptions.CultureInvariant);

    /// <summary>Ссылка важнее номера в том же тексте; прочее — null.</summary>
    public static ClashLink? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (Link.Match(text) is { Success: true } link) return new ClashLink(Guid.Parse(link.Groups[1].Value), null);
        if (Text.Match(text) is { Success: true } number) return new ClashLink(null, int.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture));
        var trimmed = text!.Trim();
        return Digits.IsMatch(trimmed) ? new ClashLink(null, int.Parse(trimmed, CultureInfo.InvariantCulture)) : null;
    }
}
