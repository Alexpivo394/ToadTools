using SmartTags.Layout.Geometry;

namespace SmartTags.Layout;

/// <summary>Неизменная часть задачи: марки, препятствия (с индексом для поиска), границы.</summary>
internal sealed class LayoutScene
{
    private readonly SpatialGrid _obstacleGrid;
    private readonly int[][] _groupNeighbors;

    public LayoutScene(LayoutRequest request)
    {
        Settings = request.Settings;
        Items = request.Items.ToArray();
        Obstacles = request.Obstacles.ToArray();
        Bounds = request.Bounds;
        BoundsShape = request.BoundsShape is { IsDegenerate: false } shape ? shape : null;

        for (var i = 0; i < Items.Length; i++) Items[i].Id = i;
        for (var i = 0; i < Obstacles.Length; i++) Obstacles[i].Id = i;

        CellSize = EstimateCellSize(Items);
        _obstacleGrid = new SpatialGrid(CellSize);
        foreach (var obstacle in Obstacles)
            _obstacleGrid.Add(obstacle.Id, obstacle.Bounds);

        AlignRadius = Math.Max(30, Settings.MaxLeader);
        _groupNeighbors = FindGroupNeighbors(Items, AlignRadius * 1.5);
    }

    public LayoutSettings Settings { get; }
    public LayoutItem[] Items { get; }
    public LayoutObstacle[] Obstacles { get; }
    public Box2D? Bounds { get; }
    public ConvexPolygon2D? BoundsShape { get; }
    public double CellSize { get; }

    /// <summary>В пределах этого расстояния соседние марки влияют друг на друга выравниванием.</summary>
    public double AlignRadius { get; }

    public void QueryObstacles(Box2D area, List<int> results) => _obstacleGrid.Query(area, results);

    public double Occupancy(Box2D area) => _obstacleGrid.Occupancy(area);

    /// <summary>Соседи по группе: близкие марки одного типа, которые лучше ставить по одну сторону.</summary>
    public int[] GroupNeighbors(int item) => _groupNeighbors[item];

    public bool IsInsideBounds(Box2D text)
    {
        if (Bounds is not { } bounds) return true;
        if (!bounds.Contains(text)) return false;
        return BoundsShape is null || text.Corners().All(BoundsShape.Contains);
    }

    private static double EstimateCellSize(LayoutItem[] items)
    {
        var heights = items.Where(item => item.TextHeight > 0).Select(item => item.TextHeight).ToList();
        var average = heights.Count > 0 ? heights.Average() : 5;
        return Math.Max(4, Math.Min(25, average * 2));
    }

    private static int[][] FindGroupNeighbors(LayoutItem[] items, double radius)
    {
        var neighbors = items.Select(_ => new List<int>()).ToArray();
        var grid = new SpatialGrid(radius);
        foreach (var item in items.Where(item => item.Group > 0 && item.Anchors.Count > 0))
            grid.Add(item.Id, Box2D.FromCenter(item.MainAnchor, 0, 0));

        var buffer = new List<int>();
        foreach (var item in items.Where(item => item.Group > 0 && item.Anchors.Count > 0))
        {
            buffer.Clear();
            grid.Query(Box2D.FromCenter(item.MainAnchor, radius * 2, radius * 2), buffer);
            foreach (var other in buffer)
            {
                if (other == item.Id || items[other].Group != item.Group) continue;
                if (Vector2D.Distance(item.MainAnchor, items[other].MainAnchor) <= radius)
                    neighbors[item.Id].Add(other);
            }
        }

        return neighbors.Select(list => list.ToArray()).ToArray();
    }
}
