using Collisions.Core;

namespace ToadTools.Tests.Collisions;

/// <summary>Подставной сервер коллизий: строки списка, запросы и действия запоминаются.</summary>
public sealed class FakeClashApi : IClashApi
{
    public List<ModelCollisionRow> Rows { get; set; } = [];
    public int Mine { get; set; }
    public int Other { get; set; }
    public int Fresh { get; set; }
    public ModelFacets Facets { get; set; } = new([], [], []);
    public Func<string?, string?, ModelResolve> Resolve { get; set; } = (_, _) => new ModelResolve("none", []);
    public Exception? Fail { get; set; }
    public List<(string Key, CollisionQuery Query, int Page)> PageRequests { get; } = [];
    public List<string> Actions { get; } = [];
    public int Visits { get; private set; }
    public Func<Guid, CollisionCardDto>? Card { get; set; }
    public string Url { get; set; } = "http://toadflow:8080";

    public Task<ModelResolve> ResolveAsync(string? path, string? name, CancellationToken ct) => Run(() => Resolve(path, name));

    public Task<ModelCollisionPage> CollisionsAsync(string key, CollisionQuery query, int page, CancellationToken ct) => Run(() =>
    {
        PageRequests.Add((key, query, page));
        var items = Rows.Skip((page - 1) * ClashApi.PageSize).Take(ClashApi.PageSize).ToList();
        return new ModelCollisionPage(Rows.Count, Mine, Other, items, Facets, Fresh);
    });

    public Task<CollisionCardDto> CardAsync(Guid id, CancellationToken ct) => Run(() =>
        Card?.Invoke(id) ?? new CollisionCardDto(Rows.Single(r => r.Collision.Id == id).Collision, []));

    public Task<IReadOnlyList<KeyValuePair<string, string>>> ElementAsync(Guid collisionId, string modelKey, string uniqueId, CancellationToken ct) =>
        Run<IReadOnlyList<KeyValuePair<string, string>>>(() => [new("ID", uniqueId)]);

    public Task<byte[]> ImageAsync(Guid id, string version, CancellationToken ct) => Run(() => new byte[] { 1 });

    public Task VisitAsync(CancellationToken ct) => Run(() => Visits++);
    public Task TakeAsync(Guid id, CancellationToken ct) => Act($"take {id}");
    public Task PassAsync(Guid id, string assignee, string note, CancellationToken ct) => Act($"assignee {id} {assignee} {note}");
    public Task SetStatusAsync(Guid id, string status, string? note, CancellationToken ct) => Act($"status {id} {status} {note}");
    public Task NoteAsync(Guid id, string text, CancellationToken ct) => Act($"note {id} {text}");
    public string ViewerUrl(Guid partId, Guid collisionId) => $"{Url}/clash/checks/{partId}?collision={collisionId}";

    private Task Act(string action) => Run(() => Actions.Add(action));

    private Task<T> Run<T>(Func<T> action) => Fail is { } fail ? Task.FromException<T>(fail) : Task.FromResult(action());

    private Task Run(Action action) => Run(() =>
    {
        action();
        return true;
    });

    /// <summary>Строка коллизии: открытая модель — сторона B (ОВ), другая — КР.</summary>
    public static ModelCollisionRow Row(int number, bool mine = true, string side = "B", Guid? partId = null) => new(
        new CollisionRowDto(Guid.NewGuid(), number, CollisionStatus.Active, mine ? CollisionAssignee.Both : CollisionAssignee.SideA, true, 30, 1000, 2000, 3000,
            "kr", "DON_KR.rvt", $"ua-{number}", number, "КР", "Стены", "Стена", "200", "Этаж 1",
            "ov", "DON_OV.rvt", $"ub-{number}", 100_000 + number, "ОВ", "Воздуховоды", "Воздуховод", "500x300", "Этаж 1",
            [], 1, 1, null, null, $"img{number}"),
        partId ?? Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "Часть 1", side, mine, mine ? null : "Петров", mine ? null : DateTimeOffset.Now);
}
