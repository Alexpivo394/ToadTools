using System.Globalization;
using ToadTools.Connection;

namespace Collisions.Core;

/// <summary>Маршруты ToadFlow, которые нужны плагину «Коллизии». Интерфейс — для тестов моделей представления.</summary>
public interface IClashApi
{
    Task<ModelResolve> ResolveAsync(string? path, string? name, CancellationToken ct);
    Task<ModelCollisionPage> CollisionsAsync(string key, CollisionQuery query, int page, CancellationToken ct);
    Task<UserInfo> MeAsync(CancellationToken ct);
    Task<CollisionCardDto> CardAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<KeyValuePair<string, string>>> ElementAsync(Guid collisionId, string modelKey, string uniqueId, CancellationToken ct);
    Task<byte[]> ImageAsync(Guid id, string version, CancellationToken ct);
    Task VisitAsync(CancellationToken ct);
    Task TakeAsync(Guid id, CancellationToken ct);
    Task PassAsync(Guid id, string assignee, string note, CancellationToken ct);
    Task SetStatusAsync(Guid id, string status, string? note, CancellationToken ct);
    Task NoteAsync(Guid id, string text, CancellationToken ct);
    string ViewerUrl(Guid partId, Guid collisionId);
}

/// <summary>Клиент коллизий ToadFlow поверх общего подключения ToadTools.</summary>
public sealed class ClashApi(ToadFlowClient client) : IClashApi
{
    public const int PageSize = 100;

    private static string E(string value) => Uri.EscapeDataString(value);

    public Task<ModelResolve> ResolveAsync(string? path, string? name, CancellationToken ct) =>
        client.GetAsync<ModelResolve>($"/api/clash/models/resolve?path={E(path ?? "")}&name={E(name ?? "")}", ct);

    public Task<ModelCollisionPage> CollisionsAsync(string key, CollisionQuery query, int page, CancellationToken ct)
    {
        var parts = new List<string> { $"status={E(query.Status)}", $"group={E(query.Group)}" };
        if (!string.IsNullOrWhiteSpace(query.Pair)) parts.Add($"pair={E(query.Pair!)}");
        if (!string.IsNullOrWhiteSpace(query.Level)) parts.Add($"level={E(query.Level!)}");
        if (!string.IsNullOrWhiteSpace(query.Search)) parts.Add($"q={E(query.Search!.Trim())}");
        parts.Add($"page={page}");
        parts.Add($"pageSize={PageSize}");
        return client.GetAsync<ModelCollisionPage>($"/api/clash/models/{E(key)}/collisions?{string.Join("&", parts)}", ct);
    }

    public Task<UserInfo> MeAsync(CancellationToken ct) => client.MeAsync(ct);

    public Task<CollisionCardDto> CardAsync(Guid id, CancellationToken ct) => client.GetAsync<CollisionCardDto>($"/api/clash/collisions/{id}", ct);

    /// <summary>Свойства элемента парами «название — значение»: сначала основные, потом параметры по алфавиту.</summary>
    public async Task<IReadOnlyList<KeyValuePair<string, string>>> ElementAsync(Guid collisionId, string modelKey, string uniqueId, CancellationToken ct)
    {
        var element = await client.GetAsync<ElementPropertiesDto>($"/api/clash/collisions/{collisionId}/elements/{E(modelKey)}/{E(uniqueId)}", ct)
            .ConfigureAwait(false);
        var result = new List<KeyValuePair<string, string>>();

        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) result.Add(new KeyValuePair<string, string>(name, value!));
        }

        Add("Модель", element.ModelName);
        Add("ID", element.ElementId.ToString(CultureInfo.InvariantCulture));
        Add("Категория", element.Category);
        Add("Семейство", element.Family);
        Add("Тип", element.Type);
        Add("Уровень", element.Level);
        Add("Рабочий набор", element.Workset);
        Add("UniqueId", element.UniqueId);
        foreach (var parameter in element.Parameters ?? [])
            Add(parameter.Name, parameter.Text ?? parameter.Number?.ToString(CultureInfo.InvariantCulture));
        return result;
    }

    public Task<byte[]> ImageAsync(Guid id, string version, CancellationToken ct) => client.GetBytesAsync($"/api/clash/collisions/{id}/image?v={E(version)}", ct);

    public Task VisitAsync(CancellationToken ct) => client.PostAsync<object>("/api/clash/my/visit", null, ct);

    public Task TakeAsync(Guid id, CancellationToken ct) => client.PostAsync<object>($"/api/clash/collisions/{id}/take", null, ct);

    public Task PassAsync(Guid id, string assignee, string note, CancellationToken ct) =>
        client.PostAsync<object>($"/api/clash/collisions/{id}/assignee", new { assignee, note }, ct);

    public Task SetStatusAsync(Guid id, string status, string? note, CancellationToken ct) =>
        client.PostAsync<object>($"/api/clash/collisions/{id}/status", new { status, note }, ct);

    public Task NoteAsync(Guid id, string text, CancellationToken ct) => client.PostAsync<object>($"/api/clash/collisions/{id}/notes", new { text }, ct);

    public string ViewerUrl(Guid partId, Guid collisionId) => client.WebUrl($"/clash/checks/{partId}?collision={collisionId}") ?? "";
}
