using ToadTools.Connection;

namespace Families.Core;

/// <summary>Маршруты ToadFlow, которые нужны панели «Семейства». Интерфейс — для тестов моделей представления.</summary>
public interface IFamiliesApi
{
    Task<UserInfo> MeAsync(CancellationToken ct);
    Task<LibraryInfo> LibraryAsync(CancellationToken ct);
    Task<FamilyPage> SearchAsync(FamilySearch search, int page, CancellationToken ct);
    Task<FamilyFacets> FacetsAsync(bool unreviewed, CancellationToken ct);
    Task<FamilyCardDto> CardAsync(Guid id, bool unreviewed, CancellationToken ct);
    Task<byte[]> PreviewAsync(Guid id, string hash, CancellationToken ct);
    Task ReportLoadAsync(Guid id, string? hash, string? project, CancellationToken ct);
    Task<List<ProjectFamilyStatus>> ProjectStatusAsync(IReadOnlyList<ProjectFamilyDto> families, bool unreviewed, CancellationToken ct);
    Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct);

    /// <summary>Мои заявки с изменениями, которых я не видел.</summary>
    Task<List<RequestItem>> RequestUpdatesAsync(CancellationToken ct);

    /// <summary>Открытые заявки — для «эта публикация закрывает заявку».</summary>
    Task<List<RequestItem>> OpenRequestsAsync(CancellationToken ct);

    Task MarkRequestSeenAsync(Guid id, CancellationToken ct);

    /// <summary>Адрес страницы веба; пусто — подключения нет.</summary>
    string WebUrl(string relative);
}

/// <summary>Запрос поиска: строка и фильтры, как в вебе.</summary>
public sealed record FamilySearch(string? Text, string? Section, string? Category, bool Unreviewed);

/// <summary>Клиент семейств ToadFlow поверх общего подключения ToadTools.</summary>
public sealed class FamiliesApi(ToadFlowClient client) : IFamiliesApi
{
    public const int PageSize = 50;

    private static string E(string value) => Uri.EscapeDataString(value);

    public Task<UserInfo> MeAsync(CancellationToken ct) => client.MeAsync(ct);

    public Task<LibraryInfo> LibraryAsync(CancellationToken ct) => client.GetAsync<LibraryInfo>("/api/families/library", ct);

    public Task<FamilyPage> SearchAsync(FamilySearch search, int page, CancellationToken ct)
    {
        var parts = new List<string> { $"page={page}", $"pageSize={PageSize}" };
        if (!string.IsNullOrWhiteSpace(search.Text)) parts.Add($"q={E(search.Text!.Trim())}");
        if (!string.IsNullOrWhiteSpace(search.Section)) parts.Add($"section={E(search.Section!)}");
        if (!string.IsNullOrWhiteSpace(search.Category)) parts.Add($"category={E(search.Category!)}");
        if (search.Unreviewed) parts.Add("unreviewed=true");
        return client.GetAsync<FamilyPage>($"/api/families/search?{string.Join("&", parts)}", ct);
    }

    public Task<FamilyFacets> FacetsAsync(bool unreviewed, CancellationToken ct) =>
        client.GetAsync<FamilyFacets>($"/api/families/facets{(unreviewed ? "?unreviewed=true" : "")}", ct);

    public Task<FamilyCardDto> CardAsync(Guid id, bool unreviewed, CancellationToken ct) =>
        client.GetAsync<FamilyCardDto>($"/api/families/{id}{(unreviewed ? "?unreviewed=true" : "")}", ct);

    public Task<byte[]> PreviewAsync(Guid id, string hash, CancellationToken ct) =>
        client.GetBytesAsync($"/api/families/{id}/preview?version={E(hash)}&unreviewed=true", ct);

    public Task ReportLoadAsync(Guid id, string? hash, string? project, CancellationToken ct) =>
        client.PostAsync<object>($"/api/families/{id}/loads", new { hash, project }, ct);

    public async Task<List<ProjectFamilyStatus>> ProjectStatusAsync(IReadOnlyList<ProjectFamilyDto> families, bool unreviewed, CancellationToken ct) =>
        await client.PostAsync<List<ProjectFamilyStatus>>("/api/families/project-status", new { families, unreviewed }, ct) ?? [];

    public async Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct) =>
        await client.PostAsync<PublishResult>("/api/families/publications", request, ct) ?? throw new ToadFlowRequestException(200, "ToadFlow не ответил на публикацию");

    public Task<List<RequestItem>> RequestUpdatesAsync(CancellationToken ct) => client.GetAsync<List<RequestItem>>("/api/families/requests/updates", ct);

    public async Task<List<RequestItem>> OpenRequestsAsync(CancellationToken ct) =>
        (await client.GetAsync<RequestPage>("/api/families/requests?scope=all&state=New,InWork&pageSize=200", ct)).Items;

    public Task MarkRequestSeenAsync(Guid id, CancellationToken ct) => client.PostAsync<object>($"/api/families/requests/{id}/seen", null, ct);

    public string WebUrl(string relative) => client.WebUrl(relative) ?? "";
}
