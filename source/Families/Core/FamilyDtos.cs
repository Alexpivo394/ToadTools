namespace Families.Core;

/// <summary>Библиотека: где лежат файлы и может ли вошедший публиковать (<c>GET /api/families/library</c>).</summary>
public sealed record LibraryInfo(bool Configured, string Root, string ReviewFolder, bool CanPublish);

/// <summary>Семейство в выдаче поиска.</summary>
/// <param name="State"><c>Approved</c>, <c>InReview</c>, <c>Archived</c>.</param>
/// <param name="PreviewHash">Хеш действующей версии с картинкой; null — картинки нет.</param>
public sealed record FamilyHit(
    Guid Id,
    string Name,
    string Section,
    string RelativePath,
    string Category,
    int? RevitYear,
    int TypeCount,
    string State,
    bool HasPreview,
    DateTimeOffset UpdatedAt,
    string? PreviewHash,
    int LoadCount);

public sealed record FamilyPage(List<FamilyHit> Items, int Total);

public sealed record FacetValue(string Value, int Count);

public sealed record FamilyFacets(List<FacetValue> Sections, List<FacetValue> Categories, List<FacetValue> Years, List<FacetValue> Placements);

public sealed record FamilyTypeInfo(string Name, Dictionary<string, string> Values);

public sealed record FamilyContentDto(string Category, string Placement, int RevitYear, List<FamilyTypeInfo> Types, bool TypesFromCatalog);

public sealed record FamilyVersionDto(
    Guid Id,
    string Hash,
    int? RevitYear,
    string State,
    string Note,
    string RelativePath,
    DateTimeOffset SeenAt,
    bool IsCurrent,
    string? AuthorName,
    string? Comment);

/// <summary>Карточка семейства (<c>GET /api/families/{id}</c>).</summary>
/// <param name="FullPath">Где лежит файл: корень библиотеки и путь от него.</param>
/// <param name="Content">Содержимое действующей версии; null — ферма ещё не прочитала.</param>
public sealed record FamilyCardDto(
    Guid Id,
    string Name,
    string RelativePath,
    string FullPath,
    string Section,
    string State,
    string Category,
    string Placement,
    int? RevitYear,
    int TypeCount,
    string? Description,
    string? Manufacturer,
    DateTimeOffset UpdatedAt,
    FamilyContentDto? Content,
    List<FamilyVersionDto> Versions,
    int LoadCount,
    DateTimeOffset? LastLoadedAt)
{
    public FamilyVersionDto? Current => Versions.FirstOrDefault(v => v.IsCurrent);
}

/// <summary>Семейство открытого проекта для сверки с библиотекой; карточка и хеш — из метки.</summary>
public sealed record ProjectFamilyDto(string Name, Guid? CardId, string? Hash);

/// <param name="State"><c>Current</c>, <c>Outdated</c>, <c>NotFromLibrary</c>, <c>Archived</c>.</param>
/// <param name="CardId">Карточка из метки или предложенная по имени.</param>
/// <param name="CurrentHash">Хеш действующей версии: с ним сверяется файл перед обновлением.</param>
public sealed record ProjectFamilyStatus(
    string Name,
    string State,
    Guid? CardId,
    string? CardName,
    string? CurrentHash,
    int? RevitYear,
    string? FullPath,
    DateTimeOffset? UpdatedAt);

public sealed record PublishRequest(string Path, string Hash, Guid? CardId, string? Comment, Guid? RequestId);

/// <param name="ScanStarted">Обход начат сразу; иначе — сразу после текущего.</param>
public sealed record PublishResult(Guid PublicationId, string RelativePath, bool ScanStarted);

/// <summary>Заявка на семейство в списке.</summary>
/// <param name="State"><c>New</c>, <c>InWork</c>, <c>Done</c>, <c>Exists</c>, <c>Rejected</c>.</param>
/// <param name="Unread">Есть изменения, которых участник не видел.</param>
public sealed record RequestItem(
    Guid Id,
    int Number,
    string Title,
    string State,
    string? AssigneeName,
    Guid? CardId,
    string? CardName,
    bool Unread,
    DateTimeOffset UpdatedAt);

public sealed record RequestPage(List<RequestItem> Items, int Total);
