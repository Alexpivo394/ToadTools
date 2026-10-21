namespace ParamChecker.Core.Checking;

public enum ModelStatus
{
    Checked,
    Failed,
    Skipped
}

/// <summary>Чем закончилась одна модель запуска.</summary>
/// <param name="Report">Итог проверки; есть только у проверенной модели.</param>
/// <param name="ReportPath">Куда сохранён отчёт Excel.</param>
public sealed record ModelOutcome(
    string ProfileName,
    string ModelName,
    string SourcePath,
    ModelStatus Status,
    TimeSpan Duration,
    ModelReport? Report = null,
    string? Error = null,
    string? ReportPath = null);
