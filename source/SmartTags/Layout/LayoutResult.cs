namespace SmartTags.Layout;

public enum ConflictKind
{
    TextOverText,
    TextOverTag,
    TextOutsideCrop,
    TextOverElement,
    LeaderOverLeader,
    LeaderOverText,
    LeaderOverElement,
    LeaderOverTagLeader,
    LeaderTooLong,
    TooClose,
    SharedAnchor
}

/// <summary>Конфликт марки <see cref="Item" /> с другой маркой или препятствием.</summary>
public sealed record Conflict(ConflictKind Kind, int Item, int OtherItem, int Obstacle, bool IsHard)
{
    public static string Describe(ConflictKind kind) => kind switch
    {
        ConflictKind.TextOverText => "текст на тексте",
        ConflictKind.TextOverTag => "текст на аннотации",
        ConflictKind.TextOutsideCrop => "текст вне подрезки",
        ConflictKind.TextOverElement => "текст на элементе",
        ConflictKind.LeaderOverLeader => "выноски пересекаются",
        ConflictKind.LeaderOverText => "выноска через текст",
        ConflictKind.LeaderOverElement => "выноска через элемент",
        ConflictKind.LeaderOverTagLeader => "выноска через выноску марки",
        ConflictKind.LeaderTooLong => "выноска длиннее максимума",
        ConflictKind.TooClose => "слишком близко",
        ConflictKind.SharedAnchor => "общая точка привязки",
        _ => kind.ToString()
    };
}

public enum PlacementStatus
{
    NotPlaced,
    Placed,
    PlacedWithConflicts
}

public sealed class LayoutStats
{
    public int Items { get; set; }
    public int Obstacles { get; set; }
    public int Candidates { get; set; }
    public int Iterations { get; set; }
    public int Stacks { get; set; }
    public int Groups { get; set; }
    public int AlignMoves { get; set; }
    public int HardConflicts { get; set; }
    public int BudgetMs { get; set; }
    public long ElapsedMs { get; set; }
    public bool Cancelled { get; set; }
}

public sealed class LayoutResult(int itemCount)
{
    public Placement?[] Placements { get; } = new Placement?[itemCount];
    public PlacementStatus[] Statuses { get; } = new PlacementStatus[itemCount];
    public List<Conflict> Conflicts { get; } = [];
    public LayoutStats Stats { get; } = new();
}

/// <summary>Сообщение о ходе раскладки для окна прогресса.</summary>
public sealed record LayoutProgress(string Stage, int Current, int Total, int HardConflicts = -1);
