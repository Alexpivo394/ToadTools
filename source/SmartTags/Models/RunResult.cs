using SmartTags.Layout;

namespace SmartTags.Models;

/// <summary>Итог по одной марке для отчёта.</summary>
public sealed class ItemReport(string label, IReadOnlyList<ElementId> elementIds, ItemStatus status, string reason)
{
    public string Label { get; } = label;
    public IReadOnlyList<ElementId> ElementIds { get; } = elementIds;
    public ItemStatus Status { get; } = status;
    public string Reason { get; } = reason;

    public string StatusText => Status switch
    {
        ItemStatus.Placed => "Размещена",
        ItemStatus.PlacedWithConflicts => "Конфликт",
        ItemStatus.Skipped => "Пропущен",
        _ => "Ошибка"
    };

    public bool IsProblem => Status is ItemStatus.PlacedWithConflicts or ItemStatus.Failed;

    public static string Describe(SkipReason reason) => reason switch
    {
        SkipReason.AlreadyTagged => "Уже промаркирован",
        SkipReason.TooShort => "Короткий участок",
        SkipReason.SameSection => "Повтор: трасса уже подписана на другом участке",
        SkipReason.Vertical => "Вертикальный участок",
        SkipReason.OutsideCrop => "Вне подрезки вида",
        SkipReason.NoAnchor => "Нет точки привязки",
        SkipReason.CreateFailed => "Не удалось создать марку",
        SkipReason.MeasureFailed => "Не удалось измерить марку",
        SkipReason.PlaceFailed => "Не удалось разместить марку",
        SkipReason.Cancelled => "Отменено",
        _ => string.Empty
    };
}

public sealed class RunResult
{
    public bool Completed { get; set; }
    public bool Cancelled { get; set; }
    public string? Error { get; set; }

    public List<ItemReport> Items { get; } = [];
    public LayoutStats? LayoutStats { get; set; }
    public long ElapsedMs { get; set; }
    public string? LogPath { get; set; }

    /// <summary>3D-вид был заблокирован при запуске (если создана копия — её Id).</summary>
    public bool ViewLocked { get; set; }

    public ElementId? LockedCopyId { get; set; }

    public int Count(ItemStatus status) => Items.Count(item => item.Status == status);

    public List<ElementId> ProblemElementIds => Items
        .Where(item => item.IsProblem)
        .SelectMany(item => item.ElementIds)
        .Distinct()
        .ToList();
}
