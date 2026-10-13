namespace SmartTags.Layout.Geometry;

/// <summary>Равномерная сетка для быстрого поиска объектов (по их индексам) в окрестности прямоугольника.</summary>
public sealed class SpatialGrid
{
    // Защита от гигантских коробок (ошибочная геометрия): не раскладываем объект больше чем на 4000 ячеек по оси.
    private const int MaxCellsPerAxis = 4000;

    private readonly Dictionary<long, List<int>> _cells = new();
    private readonly HashSet<int> _seen = new();

    public SpatialGrid(double cellSize)
    {
        if (!(cellSize > 0) || double.IsInfinity(cellSize))
            throw new ArgumentOutOfRangeException(nameof(cellSize));

        CellSize = cellSize;
    }

    public double CellSize { get; }

    public void Add(int id, Box2D bounds)
    {
        if (bounds.IsEmpty) return;

        GetRange(bounds, out var x0, out var y0, out var x1, out var y1);
        for (var x = x0; x <= x1; x++)
        for (var y = y0; y <= y1; y++)
        {
            var key = Key(x, y);
            if (!_cells.TryGetValue(key, out var cell))
            {
                cell = new List<int>(4);
                _cells[key] = cell;
            }

            cell.Add(id);
        }
    }

    public void Remove(int id, Box2D bounds)
    {
        if (bounds.IsEmpty) return;

        GetRange(bounds, out var x0, out var y0, out var x1, out var y1);
        for (var x = x0; x <= x1; x++)
        for (var y = y0; y <= y1; y++)
        {
            var key = Key(x, y);
            if (!_cells.TryGetValue(key, out var cell)) continue;

            cell.Remove(id);
            if (cell.Count == 0) _cells.Remove(key);
        }
    }

    /// <summary>Добавляет в <paramref name="results" /> индексы объектов, чьи ячейки задевает прямоугольник (без повторов).</summary>
    public void Query(Box2D bounds, List<int> results)
    {
        if (bounds.IsEmpty) return;

        _seen.Clear();
        GetRange(bounds, out var x0, out var y0, out var x1, out var y1);
        for (var x = x0; x <= x1; x++)
        for (var y = y0; y <= y1; y++)
        {
            if (!_cells.TryGetValue(Key(x, y), out var cell)) continue;

            foreach (var id in cell)
            {
                if (_seen.Add(id)) results.Add(id);
            }
        }
    }

    /// <summary>Доля занятых ячеек внутри прямоугольника, 0..1 — мера «тесноты» места.</summary>
    public double Occupancy(Box2D bounds)
    {
        if (bounds.IsEmpty) return 0;

        GetRange(bounds, out var x0, out var y0, out var x1, out var y1);
        var occupied = 0;
        for (var x = x0; x <= x1; x++)
        for (var y = y0; y <= y1; y++)
        {
            if (_cells.ContainsKey(Key(x, y))) occupied++;
        }

        var total = (double)(x1 - x0 + 1) * (y1 - y0 + 1);
        return Math.Min(1, occupied / total);
    }

    private void GetRange(Box2D bounds, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = (int)Math.Floor(bounds.MinX / CellSize);
        y0 = (int)Math.Floor(bounds.MinY / CellSize);
        x1 = Math.Min((int)Math.Floor(bounds.MaxX / CellSize), x0 + MaxCellsPerAxis);
        y1 = Math.Min((int)Math.Floor(bounds.MaxY / CellSize), y0 + MaxCellsPerAxis);
    }

    private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
}
