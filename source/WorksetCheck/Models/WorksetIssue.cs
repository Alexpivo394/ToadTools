namespace WorksetCheck.Models;

/// <summary>Что кнопка «Исправить» сделает с замечанием.</summary>
public enum IssueAction
{
    /// <summary>Перенести в существующий РН.</summary>
    Move,

    /// <summary>Создать РН и перенести в него.</summary>
    CreateAndMove,

    /// <summary>Нужный РН не определить — собрать элемент на 3D-виде, пользователь раскидает сам.</summary>
    ShowOn3D,

    /// <summary>Автоматически не исправить — причина в <see cref="WorksetIssue.ManualReason" />.</summary>
    Manual
}

/// <summary>Элемент, лежащий не в своём рабочем наборе.</summary>
public sealed class WorksetIssue
{
    public required ElementId ElementId { get; init; }
    public required string Category { get; init; }
    public required string Element { get; init; }
    public required string Workset { get; init; }
    public required string Problem { get; init; }
    public required IssueAction Action { get; init; }

    /// <summary>РН, куда переносим (для <see cref="IssueAction.Move" /> и <see cref="IssueAction.CreateAndMove" />).</summary>
    public string? TargetWorkset { get; init; }

    public string? ManualReason { get; init; }

    public long Id => ElementId.Value();

    public bool IsAutoFix => Action is IssueAction.Move or IssueAction.CreateAndMove;

    public string ActionText => Action switch
    {
        IssueAction.Move => $"Перенести в «{TargetWorkset}»",
        IssueAction.CreateAndMove => $"Создать РН «{TargetWorkset}» и перенести",
        IssueAction.ShowOn3D => "На 3D-вид — распределить вручную",
        _ => "Вручную: " + ManualReason
    };
}
