namespace WorksetCheck.Models;

public sealed class CheckResult(IReadOnlyList<WorksetIssue> issues, int checkedElements, IReadOnlyList<string> closedWorksets)
{
    public IReadOnlyList<WorksetIssue> Issues { get; } = issues;

    /// <summary>Сколько элементов проверено (без вспомогательных: эскизов, вложенных семейств и т.п.).</summary>
    public int CheckedElements { get; } = checkedElements;

    /// <summary>Закрытые пользовательские РН — их элементы не проверялись.</summary>
    public IReadOnlyList<string> ClosedWorksets { get; } = closedWorksets;

    public int Count(IssueAction action) => Issues.Count(issue => issue.Action == action);
}

public sealed class FixResult
{
    public int Moved { get; set; }
    public List<string> CreatedWorksets { get; } = [];

    /// <summary>Вид «Проверка РН»; null — собирать на нём было нечего.</summary>
    public ElementId? ViewId { get; set; }

    public int ShownOn3D { get; set; }
    public List<string> Failures { get; } = [];
}
