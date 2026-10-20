using System.ComponentModel;

namespace SmartTags.Models;

public enum ViewKind
{
    Plan,
    Section,
    ThreeD
}

/// <summary>Форма сечения воздуховодов: круглые и прямоугольные маркируются отдельными типами марок.</summary>
public enum DuctProfile
{
    Any,
    Round,
    Rectangular
}

/// <summary>Что делать с элементами, у которых на виде уже есть марка.</summary>
public enum ExistingTagMode
{
    [Description("Пропустить уже подписанные")]
    Skip,

    [Description("Переразместить вместе с новыми")]
    Relayout,

    [Description("Удалить и создать заново")]
    Recreate
}

/// <summary>Сколько марок ставить на трубы, воздуховоды, лотки и короба.</summary>
public enum RunTagMode
{
    [Description("Каждый элемент")]
    Each,

    [Description("Каждый прямой участок")]
    Runs,

    [Description("Трассу целиком")]
    Routes
}

public enum SkipReason
{
    None,
    AlreadyTagged,
    TooShort,
    SameSection,
    Vertical,
    OutsideCrop,
    NoAnchor,
    CreateFailed,
    MeasureFailed,
    PlaceFailed,
    Cancelled
}

public enum ItemStatus
{
    Placed,
    PlacedWithConflicts,
    Skipped,
    Failed
}
