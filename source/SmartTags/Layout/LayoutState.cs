using SmartTags.Layout.Geometry;

namespace SmartTags.Layout;

/// <summary>Текущее положение всех марок с индексом для поиска соседей.</summary>
internal sealed class LayoutState(LayoutScene scene)
{
    private readonly Placement?[] _placements = new Placement?[scene.Items.Length];
    private readonly SpatialGrid _grid = new(scene.CellSize);

    public Placement? this[int item] => _placements[item];

    public void Set(int item, Placement? placement)
    {
        if (_placements[item] is { } old)
            _grid.Remove(item, old.Bounds);

        _placements[item] = placement;
        if (placement is not null)
            _grid.Add(item, placement.Bounds);
    }

    public void QueryNeighbors(Box2D area, List<int> results) => _grid.Query(area, results);

    public Placement?[] Save() => (Placement?[])_placements.Clone();

    public void Restore(Placement?[] saved)
    {
        for (var i = 0; i < _placements.Length; i++)
        {
            if (!ReferenceEquals(_placements[i], saved[i]))
                Set(i, saved[i]);
        }
    }
}
