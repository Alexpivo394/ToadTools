using ClassifierCode.Engine.Facts;

namespace ClassifierCode.Engine.Matching;

/// <summary>Что известно при проверке одного элемента: факты, режим, домен и уже определённый тип.</summary>
public sealed record MatchContext(IElementFacts Facts, string ModeKey, NetworkDomain EffectiveDomain)
{
    /// <summary>Тип элемента, если уже определён (для шагов раздела и уточнений).</summary>
    public string? TypeKey { get; init; }

    /// <summary>Текст, на котором сработал текущий шаг ("$source").</summary>
    public string? StepSource { get; init; }

    /// <summary>Прочитанные источники текста этого элемента.</summary>
    public Dictionary<string, string> TextCache { get; init; } = new();
}
