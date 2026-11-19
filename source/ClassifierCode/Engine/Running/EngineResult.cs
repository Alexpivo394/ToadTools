using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Running;

public enum EngineStatus
{
    Classified,
    Skipped,
    Unclassified
}

/// <summary>Строка трассы: на какой стадии и что сработало.</summary>
public sealed record TraceStep(string Stage, string Text);

/// <summary>Итог по элементу: статус, раздел и тип (ключи), позиция, причина и трасса.</summary>
public sealed record EngineResult(
    EngineStatus Status,
    string? Discipline,
    string? Type,
    Position? Position,
    string? Reason,
    IReadOnlyList<TraceStep> Trace);
