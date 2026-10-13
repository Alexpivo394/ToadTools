using SmartTags.Layout.Geometry;

namespace SmartTags.Layout;

/// <summary>
///     Разбирает плотные скопления конфликтующих марок: выстраивает их тексты аккуратным столбиком
///     сбоку от скопления, если так получается лучше.
/// </summary>
internal sealed class StackBuilder(LayoutScene scene, PlacementScorer scorer)
{
    private readonly LayoutSettings _s = scene.Settings;
    private readonly List<int> _buffer = new(64);

    public int Built { get; private set; }

    public void Run(LayoutState state, Func<bool> cancelled)
    {
        foreach (var cluster in FindClusters(state))
        {
            if (cancelled()) return;
            if (cluster.Count >= 2) TryStack(state, cluster);
        }
    }

    /// <summary>Группы марок с жёсткими конфликтами: связаны конфликтом или близкими точками привязки.</summary>
    private List<List<int>> FindClusters(LayoutState state)
    {
        var count = scene.Items.Length;
        var sets = new DisjointSets(count);
        var troubled = new bool[count];
        var conflicts = new List<Conflict>();

        for (var i = 0; i < count; i++)
        {
            if (state[i] is not { } placed) continue;

            conflicts.Clear();
            if (scorer.Full(scene.Items[i], placed, state, conflicts).Hard == 0) continue;

            troubled[i] = true;
            foreach (var conflict in conflicts.Where(c => c.IsHard && c.OtherItem >= 0))
            {
                sets.Union(i, conflict.OtherItem);
                troubled[conflict.OtherItem] = true;
            }
        }

        var flagged = Enumerable.Range(0, count).Where(i => troubled[i]).ToList();
        for (var a = 0; a < flagged.Count; a++)
        for (var b = a + 1; b < flagged.Count; b++)
        {
            var distance = Vector2D.Distance(scene.Items[flagged[a]].MainAnchor, scene.Items[flagged[b]].MainAnchor);
            if (distance < _s.MaxLeader) sets.Union(flagged[a], flagged[b]);
        }

        return flagged
            .GroupBy(sets.Find)
            .Select(group => group.ToList())
            .OrderByDescending(group => group.Count)
            .ToList();
    }

    private void TryStack(LayoutState state, List<int> cluster)
    {
        var area = Box2D.Empty;
        var textHeight = 0.0;
        foreach (var index in cluster)
        {
            var item = scene.Items[index];
            area = area.Union(item.MainAnchor);
            if (item.Footprint is not null) area = area.Union(item.Footprint.Bounds);
            textHeight = Math.Max(textHeight, item.TextHeight);
        }

        if (area.IsEmpty || textHeight <= 0) return;

        var pitch = textHeight + Math.Max(_s.MinGap, _s.SoftGap * 0.6);
        var current = cluster.Select(index => state[index]).ToArray();
        var bestScore = ClusterScore(state, cluster, current);
        Placement?[]? best = null;

        double[] offsets = [_s.PreferredLeader, (_s.PreferredLeader + _s.MaxLeader) / 2, _s.MaxLeader * 0.85];
        double[] shifts = [0, pitch / 2, -pitch / 2, pitch, -pitch];

        foreach (var side in new[] { ShelfSide.Left, ShelfSide.Right })
        foreach (var offset in offsets)
        foreach (var shift in shifts)
        {
            var variant = BuildColumn(cluster, area, side, offset, shift, pitch, current);
            if (variant is null) continue;

            var score = ClusterScore(state, cluster, variant);
            if (!score.IsBetterThan(bestScore)) continue;

            bestScore = score;
            best = variant;
        }

        if (best is null) return;

        for (var i = 0; i < cluster.Count; i++)
        {
            if (best[i] is { } placement && !ReferenceEquals(placement, current[i]))
                placement.Stacked = true;

            state.Set(cluster[i], best[i]);
        }

        Built++;
    }

    /// <summary>
    ///     Столбик текстов на вертикали слева/справа от скопления, в порядке высоты точек привязки
    ///     (так выноски не перекрещиваются). Марки, до которых не дотянуться, остаются на месте.
    /// </summary>
    private Placement?[]? BuildColumn(List<int> cluster, Box2D area, ShelfSide side, double offset, double shift,
        double pitch, Placement?[] fallback)
    {
        var edge = side == ShelfSide.Left ? area.MinX - offset : area.MaxX + offset;
        var top = area.Center.Y + shift - (cluster.Count - 1) * pitch / 2;
        var order = Enumerable.Range(0, cluster.Count)
            .OrderBy(i => scene.Items[cluster[i]].MainAnchor.Y)
            .ToArray();

        var result = new Placement?[cluster.Count];
        var moved = false;
        for (var row = 0; row < order.Length; row++)
        {
            var slot = order[row];
            var item = scene.Items[cluster[slot]];
            var centerY = top + row * pitch;
            var text = side == ShelfSide.Left
                ? new Box2D(edge - item.TextWidth, centerY - item.TextHeight / 2, edge, centerY + item.TextHeight / 2)
                : new Box2D(edge, centerY - item.TextHeight / 2, edge + item.TextWidth, centerY + item.TextHeight / 2);

            var elbow = new Vector2D(side == ShelfSide.Left ? edge + _s.Shelf : edge - _s.Shelf, centerY);
            var anchor = NearestAnchor(item, elbow, out var distance);
            if (distance > _s.MaxLeader)
            {
                result[slot] = fallback[slot];
                continue;
            }

            result[slot] = Placement.FromText(item, anchor, text, side, _s.Shelf);
            moved = true;
        }

        return moved ? result : null;
    }

    private static int NearestAnchor(LayoutItem item, Vector2D point, out double distance)
    {
        var nearest = 0;
        distance = double.MaxValue;
        for (var i = 0; i < item.Anchors.Count; i++)
        {
            var d = Vector2D.Distance(item.Anchors[i], point);
            if (d >= distance) continue;

            distance = d;
            nearest = i;
        }

        return nearest;
    }

    private Score ClusterScore(LayoutState state, List<int> cluster, Placement?[] placements)
    {
        var members = new HashSet<int>(cluster);
        var score = Score.Zero;
        for (var i = 0; i < cluster.Count; i++)
        {
            if (placements[i] is not { } placement) continue;

            var item = scene.Items[cluster[i]];
            score += scorer.Own(item, placement);

            for (var j = i + 1; j < cluster.Count; j++)
            {
                if (placements[j] is { } other)
                    score += scorer.Pair(item, placement, scene.Items[cluster[j]], other);
            }

            _buffer.Clear();
            state.QueryNeighbors(placement.Bounds.Inflate(scorer.AlignRadius), _buffer);
            foreach (var outsider in _buffer)
            {
                if (!members.Contains(outsider) && state[outsider] is { } other)
                    score += scorer.Pair(item, placement, scene.Items[outsider], other);
            }
        }

        return score;
    }
}

/// <summary>Система непересекающихся множеств (union-find) по индексам 0..n-1.</summary>
internal sealed class DisjointSets
{
    private readonly int[] _parent;

    public DisjointSets(int count)
    {
        _parent = Enumerable.Range(0, count).ToArray();
    }

    public int Find(int x)
    {
        while (_parent[x] != x)
        {
            _parent[x] = _parent[_parent[x]];
            x = _parent[x];
        }

        return x;
    }

    public void Union(int a, int b)
    {
        var rootA = Find(a);
        var rootB = Find(b);
        if (rootA != rootB) _parent[rootA] = rootB;
    }
}
