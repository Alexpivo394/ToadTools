using Collisions.Core;
using ToadTools.Connection;

namespace ToadTools.Tests.Collisions;

/// <summary>Подставной сервер коллизий: строки списка, запросы и действия запоминаются.</summary>
public sealed class FakeClashApi : IClashApi
{
    public List<ModelCollisionRow> Rows { get; set; } = [];
    public int Mine { get; set; }
    public int Other { get; set; }
    public int Fresh { get; set; }

    /// <summary>«Новые для меня» в ответе; null — старый сервер без этого поля.</summary>
    public int? NewForMe { get; set; }

    public List<Guid> Seen { get; } = [];
    public Exception? SeenFail { get; set; }
    public ModelFacets Facets { get; set; } = new([], [], []);
    public Func<string?, string?, ModelResolve> Resolve { get; set; } = (_, _) => new ModelResolve("none", []);
    public Exception? Fail { get; set; }
    public List<(string Key, CollisionQuery Query, int Page)> PageRequests { get; } = [];
    public List<string> Actions { get; } = [];
    public int Visits { get; private set; }
    public Func<Guid, CollisionCardDto>? Card { get; set; }
    public string Url { get; set; } = "http://toadflow:8080";
    public UserInfo Me { get; set; } = new(Guid.NewGuid(), "ivanov", "Иванов", "Designer", ["ОВ"]);
    public int CardRequests { get; private set; }
    public int MeRequests { get; private set; }

    public Task<ModelResolve> ResolveAsync(string? path, string? name, CancellationToken ct) => Run(() => Resolve(path, name));

    public Task<ModelCollisionPage> CollisionsAsync(string key, CollisionQuery query, int page, CancellationToken ct) => Run(() =>
    {
        PageRequests.Add((key, query, page));
        var rows = query.Since == "unseen" ? Rows.Where(r => r.Unseen).ToList() : Rows;
        if (!string.IsNullOrWhiteSpace(query.Search)) rows = rows.Where(r => r.Collision.Number.ToString() == query.Search!.Trim()).ToList();
        var items = rows.Skip((page - 1) * ClashApi.PageSize).Take(ClashApi.PageSize).ToList();
        return new ModelCollisionPage(rows.Count, Mine, Other, items, Facets, Fresh, NewForMe);
    });

    public Task<UserInfo> MeAsync(CancellationToken ct) => Run(() =>
    {
        MeRequests++;
        return Me;
    });

    public Task<CollisionCardDto> CardAsync(Guid id, CancellationToken ct) => Run(() =>
    {
        CardRequests++;
        return Card?.Invoke(id) ?? new CollisionCardDto(Rows.Single(r => r.Collision.Id == id).Collision, []);
    });

    /// <summary>Картинку сервер не отдал: это исключение; null — отдаёт.</summary>
    public Exception? ImageFail { get; set; }

    public Task<byte[]> ImageAsync(Guid id, string version, CancellationToken ct) =>
        ImageFail is { } fail ? Task.FromException<byte[]>(fail) : Run(() => new byte[] { 1 });

    public Task VisitAsync(CancellationToken ct) => Run(() => Visits++);

    /// <summary>Отметка просмотра ждёт, пока тест не завершит эту задачу; null — отвечает сразу.</summary>
    public TaskCompletionSource? SeenGate { get; set; }

    public async Task SeenAsync(Guid id, CancellationToken ct)
    {
        if (SeenGate is { } gate) await gate.Task;
        if (SeenFail is { } fail) throw fail;
        await Run(() => Seen.Add(id));
    }

    public Task TakeAsync(Guid id, CancellationToken ct) => Act($"take {id}");
    public Task PassAsync(Guid id, string assignee, string? note, CancellationToken ct) => Act($"assignee {id} {assignee} {note}");
    public Task SetStatusAsync(Guid id, string status, string? note, CancellationToken ct) => Act($"status {id} {status} {note}");
    public Task NoteAsync(Guid id, string text, CancellationToken ct) => Act($"note {id} {text}");
    public string ViewerUrl(Guid partId, Guid collisionId) => Url.Length == 0 ? "" : $"{Url}/clash/checks/{partId}?collision={collisionId}";

    private Task Act(string action) => Run(() => Actions.Add(action));

    private Task<T> Run<T>(Func<T> action) => Fail is { } fail ? Task.FromException<T>(fail) : Task.FromResult(action());

    private Task Run(Action action) => Run(() =>
    {
        action();
        return true;
    });

    /// <summary>Строка коллизии: открытая модель — сторона B (ОВ), другая — КР.</summary>
    public static ModelCollisionRow Row(int number, bool mine = true, string side = "B", Guid? partId = null, bool unseen = false) => new(
        new CollisionRowDto(Guid.NewGuid(), number, CollisionStatus.Active, mine ? CollisionAssignee.Both : CollisionAssignee.SideA, true, 30, 1000, 2000, 3000,
            "kr", "DON_KR.rvt", $"ua-{number}", number, "КР", "Стены", "Стена", "200", "Этаж 1",
            "ov", "DON_OV.rvt", $"ub-{number}", 100_000 + number, "ОВ", "Воздуховоды", "Воздуховод", "500x300", "Этаж 1",
            [], 1, 1, null, null, $"img{number}"),
        partId ?? Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "Часть 1", side, mine, mine ? null : "Петров", mine ? null : DateTimeOffset.Now, unseen);
}
