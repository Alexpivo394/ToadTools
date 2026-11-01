namespace Collisions.Core;

/// <summary>
///     Список коллизий открытой модели: страницы по <see cref="ClashApi.PageSize" />, «Показать ещё», обновление тех же
///     страниц с сохранением выбранной строки и смена фильтров с первой страницы.
/// </summary>
public sealed class CollisionListState(IClashApi api, string modelKey)
{
    private int _pages;

    public string ModelKey => modelKey;
    public List<ModelCollisionRow> Items { get; private set; } = [];
    public int Total { get; private set; }
    public int Mine { get; private set; }
    public int Other { get; private set; }
    public ModelFacets Facets { get; private set; } = new([], [], []);
    public CollisionQuery Query { get; private set; } = new();
    public ModelCollisionRow? Selected { get; set; }
    public bool HasMore => Items.Count < Total;

    /// <summary>Первая страница заново.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var page = await api.CollisionsAsync(modelKey, Query, 1, ct).ConfigureAwait(false);
        Apply(page, page.Items.ToList(), 1);
    }

    public async Task MoreAsync(CancellationToken ct = default)
    {
        if (!HasMore) return;
        var page = await api.CollisionsAsync(modelKey, Query, _pages + 1, ct).ConfigureAwait(false);
        var known = new HashSet<Guid>(Items.Select(i => i.Collision.Id));
        Apply(page, Items.Concat(page.Items.Where(i => known.Add(i.Collision.Id))).ToList(), _pages + 1);
    }

    /// <summary>Те же страницы заново — после своего действия или «Обновить».</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var pages = Math.Max(1, _pages);
        var items = new List<ModelCollisionRow>();
        var known = new HashSet<Guid>();
        ModelCollisionPage? last = null;
        for (var number = 1; number <= pages; number++)
        {
            last = await api.CollisionsAsync(modelKey, Query, number, ct).ConfigureAwait(false);
            items.AddRange(last.Items.Where(i => known.Add(i.Collision.Id)));
            if (items.Count >= last.Total) break;
        }

        Apply(last!, items, pages);
    }

    public Task SetQueryAsync(CollisionQuery query, CancellationToken ct = default)
    {
        Query = query;
        return LoadAsync(ct);
    }

    private void Apply(ModelCollisionPage page, List<ModelCollisionRow> items, int pages)
    {
        var selectedId = Selected?.Collision.Id;
        (Items, Total, Mine, Other, Facets, _pages) = (items, page.Total, page.Mine, page.Other, page.Facets, pages);
        Selected = selectedId is { } id ? items.FirstOrDefault(i => i.Collision.Id == id) : null;
    }
}
