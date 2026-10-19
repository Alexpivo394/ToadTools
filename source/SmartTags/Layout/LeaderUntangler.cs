namespace SmartTags.Layout;

/// <summary>
///     Распутывание: если выноски двух марок перекрещиваются, марки меняются местами подписей — каждая
///     выноска идёт к подписи соседа. Два пересекающихся отрезка после обмена концов не пересекаются,
///     а подписи остаются на тех же, уже проверенных местах.
/// </summary>
internal sealed class LeaderUntangler(LayoutScene scene, PlacementScorer scorer)
{
    private readonly List<int> _buffer = new(64);
    private readonly Placement?[] _pair = new Placement?[2];
    private readonly int[] _members = new int[2];

    public int Moves { get; private set; }

    public void Run(LayoutState state, Func<bool> cancelled)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            var moved = false;
            for (var a = 0; a < scene.Items.Length; a++)
            {
                if (cancelled()) return;
                if (state[a] is not { } pa) continue;

                _buffer.Clear();
                state.QueryNeighbors(pa.Bounds, _buffer);
                foreach (var b in _buffer.ToArray())
                {
                    if (b == a || state[b] is not { } pb || !PlacementScorer.LeadersCross(pa, pb)) continue;
                    if (!TrySwap(state, a, pa, b, pb)) continue;

                    moved = true;
                    Moves++;
                    break;
                }
            }

            if (!moved) return;
        }
    }

    private bool TrySwap(LayoutState state, int a, Placement pa, int b, Placement pb)
    {
        var itemA = scene.Items[a];
        var itemB = scene.Items[b];
        _members[0] = a;
        _members[1] = b;

        _pair[0] = pa;
        _pair[1] = pb;
        var before = scorer.Group(state, _members, _pair);

        _pair[0] = Toward(itemA, pa.AnchorIndex, pb);
        _pair[1] = Toward(itemB, pb.AnchorIndex, pa);
        var after = scorer.Group(state, _members, _pair);
        if (!after.IsBetterThan(before)) return false;

        state.Set(a, _pair[0]);
        state.Set(b, _pair[1]);
        return true;
    }

    /// <summary>Подпись марки на месте подписи <paramref name="other" />: та же точка полки и сторона.</summary>
    private static Placement Toward(LayoutItem item, int anchorIndex, Placement other)
    {
        var text = Placement.TextAt(item, other.Attach, other.Side);
        return Placement.FromText(item, anchorIndex, text, other.Side, other.ShelfLength);
    }
}
