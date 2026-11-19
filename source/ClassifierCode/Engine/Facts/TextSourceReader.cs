using System.Text;
using System.Text.RegularExpressions;
using ClassifierCode.Engine.Patterns;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Facts;

/// <summary>Читает источник текста профиля у элемента.</summary>
public static class TextSourceReader
{
    public const string Separator = " | ";

    /// <summary>Части источника, которые понимает движок (без префиксов с параметром).</summary>
    public static readonly IReadOnlyList<string> SimpleParts =
    [
        "instance.name", "type.family", "type.name", "familyType", "instance.comments", "type.comments",
        "systems", "workset", "document.title"
    ];

    /// <summary>Префиксы частей: после двоеточия — имя параметра, шаблон или имя источника.</summary>
    public static readonly IReadOnlyList<string> PrefixedParts =
        ["instance:", "type:", "instanceOrType:", "firstStringMatching:", "text:"];

    public static string Read(TextSourceDef source, IElementFacts facts, IReadOnlyDictionary<string, TextSourceDef> named,
        Func<string, Regex> pattern) =>
        Read(source, facts, named, pattern, new HashSet<string>());

    private static string Read(TextSourceDef source, IElementFacts facts, IReadOnlyDictionary<string, TextSourceDef> named,
        Func<string, Regex> pattern, HashSet<string> visiting)
    {
        var sb = new StringBuilder();
        var any = false;
        for (var i = 0; i < source.Parts.Count; i++)
        {
            if (i > 0) sb.Append(Separator);
            var value = Part(source.Parts[i], facts, named, pattern, visiting);
            if (value.Length > 0) any = true;
            sb.Append(value);
        }

        var text = sb.ToString();
        if (!any && source.Fallback != null && named.TryGetValue(source.Fallback, out var fallback) && visiting.Add(source.Fallback))
        {
            text = Read(fallback, facts, named, pattern, visiting);
            visiting.Remove(source.Fallback);
        }

        if (source.Lowercase) text = text.ToLowerInvariant();
        if (source.Normalize) text = Normalize(text);
        return text;
    }

    private static string Part(string part, IElementFacts facts, IReadOnlyDictionary<string, TextSourceDef> named,
        Func<string, Regex> pattern, HashSet<string> visiting)
    {
        switch (part)
        {
            case "instance.name": return facts.InstanceName;
            case "type.family": return facts.FamilyName;
            case "type.name": return facts.TypeName;
            case "familyType": return facts.HasType ? facts.FamilyName + Separator + facts.TypeName : facts.InstanceName;
            case "instance.comments": return facts.InstanceComments;
            case "type.comments": return facts.TypeComments;
            case "systems": return facts.SystemText;
            case "workset": return facts.Workset ?? string.Empty;
            case "document.title": return facts.DocumentTitle;
        }

        var colon = part.IndexOf(':');
        if (colon < 0) return string.Empty;
        var kind = part.Substring(0, colon);
        var argument = part.Substring(colon + 1);
        switch (kind)
        {
            case "instance": return facts.InstanceParameter(argument);
            case "type": return facts.TypeParameter(argument);
            case "instanceOrType":
                var value = facts.InstanceParameter(argument);
                return string.IsNullOrWhiteSpace(value) ? facts.TypeParameter(argument) : value;
            case "firstStringMatching":
                var regex = pattern(argument);
                bool Matches(string value) => SafeMatch.IsMatch(regex, argument, value);
                return facts.InstanceStringValues().FirstOrDefault(Matches)
                       ?? facts.TypeStringValues().FirstOrDefault(Matches)
                       ?? string.Empty;
            case "text":
                if (!named.TryGetValue(argument, out var inner) || !visiting.Add(argument)) return string.Empty;
                var text = Read(inner, facts, named, pattern, visiting);
                visiting.Remove(argument);
                return text;
            default:
                return string.Empty;
        }
    }

    /// <summary>Верхний регистр и латинские буквы-двойники → кириллица (В1, набранное латиницей).</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        const string latin = "ABCEHKMOPTXY";
        const string cyrillic = "АВСЕНКМОРТХУ";
        var sb = new StringBuilder(text!.ToUpperInvariant());
        for (var i = 0; i < sb.Length; i++)
        {
            var index = latin.IndexOf(sb[i]);
            if (index >= 0) sb[i] = cyrillic[index];
        }

        return sb.ToString();
    }
}
