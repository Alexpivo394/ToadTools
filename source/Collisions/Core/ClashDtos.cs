namespace Collisions.Core;

/// <summary>Статус коллизии, как его отдаёт ToadFlow (строкой).</summary>
public enum CollisionStatus
{
    New,
    Active,
    Returned,
    Fake,
    FixedPending,
    Fixed,
    GoneByRules,
    Unchecked
}

/// <summary>Кто правит коллизию.</summary>
public enum CollisionAssignee
{
    Both,
    SideA,
    SideB
}

public sealed record ModelPart(Guid Id, string Name, DateTimeOffset? LastApprovedRunAt);

/// <param name="Key">Ключ модели в коллизиях: по нему спрашивается список.</param>
public sealed record ModelMatch(string Key, Guid Id, string Name, string ModelPath, string? Discipline, IReadOnlyList<ModelPart> Parts);

/// <param name="By"><c>path</c>, <c>name</c> или <c>none</c>.</param>
public sealed record ModelResolve(string By, IReadOnlyList<ModelMatch> Matches);

/// <summary>Коллизия, как строка списков ToadFlow.</summary>
public sealed record CollisionRowDto(
    Guid Id,
    int Number,
    CollisionStatus Status,
    CollisionAssignee Assignee,
    bool FoundNow,
    float DepthMm,
    double X,
    double Y,
    double Z,
    string ModelA,
    string ModelNameA,
    string UniqueIdA,
    long ElementIdA,
    string DisciplineA,
    string CategoryA,
    string? FamilyA,
    string? TypeA,
    string? LevelA,
    string ModelB,
    string ModelNameB,
    string UniqueIdB,
    long ElementIdB,
    string DisciplineB,
    string CategoryB,
    string? FamilyB,
    string? TypeB,
    string? LevelB,
    IReadOnlyList<string> Checks,
    int FirstVersion,
    int LastFoundVersion,
    Guid? SuggestedPredecessorId,
    int? SuggestedPredecessorNumber,
    string ImageVersion);

/// <summary>Элемент одной стороны коллизии.</summary>
public sealed record CollisionSide(string ModelKey, string ModelName, string UniqueId, long ElementId, string Discipline, string Category, string? Family,
    string? Type, string? Level)
{
    /// <summary>«Категория · семейство · тип · ID».</summary>
    public string Title => string.Join(" · ", new[] { Category, Family, Type, ElementId.ToString() }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <param name="Side">Сторона открытой модели: <c>A</c>, <c>B</c> или <c>AB</c>.</param>
/// <param name="MineToFix">Правит сторона открытой модели или обе.</param>
public sealed record ModelCollisionRow(
    CollisionRowDto Collision,
    Guid PartId,
    string PartName,
    string Side,
    bool MineToFix,
    string? TakenBy,
    DateTimeOffset? TakenAt)
{
    public CollisionSide A => new(Collision.ModelA, Collision.ModelNameA, Collision.UniqueIdA, Collision.ElementIdA, Collision.DisciplineA,
        Collision.CategoryA, Collision.FamilyA, Collision.TypeA, Collision.LevelA);

    public CollisionSide B => new(Collision.ModelB, Collision.ModelNameB, Collision.UniqueIdB, Collision.ElementIdB, Collision.DisciplineB,
        Collision.CategoryB, Collision.FamilyB, Collision.TypeB, Collision.LevelB);

    /// <summary>Свой элемент: из открытой модели (при <c>AB</c> — сторона A).</summary>
    public CollisionSide Own => Side == "B" ? B : A;

    /// <summary>Второй элемент: из другой модели (при <c>AB</c> — сторона B той же модели).</summary>
    public CollisionSide Other => Side == "B" ? A : B;
}

public sealed record Facet(string Value, string Label, int Count);

public sealed record ModelFacets(IReadOnlyList<Facet> Pairs, IReadOnlyList<Facet> Levels, IReadOnlyList<Facet> Statuses);

public sealed record ModelCollisionPage(int Total, int Mine, int Other, IReadOnlyList<ModelCollisionRow> Items, ModelFacets Facets);

/// <summary>Запись ленты коллизии.</summary>
public sealed record FeedEntryDto(
    DateTimeOffset At,
    string Kind,
    string? UserName,
    string Text,
    CollisionStatus? FromStatus,
    CollisionStatus? ToStatus,
    int? Version,
    float? DepthMm,
    Guid? UserId);

public sealed record CollisionCardDto(CollisionRowDto Collision, IReadOnlyList<FeedEntryDto> Feed);

/// <summary>Свойства элемента из выгрузки (<c>/elements/{model}/{uniqueId}</c>).</summary>
public sealed record ElementPropertiesDto(
    string ModelName,
    string? Discipline,
    string UniqueId,
    long ElementId,
    string? Category,
    string? Family,
    string? Type,
    string? Level,
    string? Workset,
    IReadOnlyList<ElementParameterDto>? Parameters);

public sealed record ElementParameterDto(string Name, string? Text, double? Number);

/// <param name="Status"><c>open</c>, <c>fake</c>, <c>fixed</c>, <c>all</c>.</param>
/// <param name="Group"><c>mine</c>, <c>other</c>, <c>all</c>.</param>
/// <param name="Level">Ключ уровня; <c>-</c> — без уровня.</param>
/// <param name="Search">№, ID элемента или UniqueId.</param>
public sealed record CollisionQuery(string Status = "open", string Group = "all", string? Pair = null, string? Level = null, string? Search = null);
