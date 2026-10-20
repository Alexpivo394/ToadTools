namespace ToadExporter.Core;

/// <summary>Что получилось с одной моделью.</summary>
public sealed class ModelResult
{
    public string Name { get; set; } = "";

    public StepResult Rvt { get; set; }

    public StepResult Nwc { get; set; }

    /// <summary>Причина пропуска или текст ошибки.</summary>
    public string? Note { get; set; }

    public TimeSpan Elapsed { get; set; }

    /// <summary>Нажали «Пропустить модель» или «Прервать».</summary>
    public bool SkippedByUser { get; set; }

    public bool HasErrors => Rvt == StepResult.Failed || Nwc == StepResult.Failed;

    public bool IsExported => !SkippedByUser && !HasErrors && (Rvt == StepResult.Done || Nwc == StepResult.Done);

    /// <summary>Ни ошибок, ни пропуска, но и выгружать было нечего — например, нет вида для NWC.</summary>
    public bool IsNothingExported => !SkippedByUser && !HasErrors && !IsExported;
}

public enum StepResult
{
    NotRequested,
    Done,
    Skipped,
    Failed
}
