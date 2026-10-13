namespace SmartTags.Layout;

/// <summary>
///     Доводка: если кромка текста почти совпадает с кромкой соседа (по ту же сторону полки),
///     подтягивает текст в линию — меняя длину полки или сдвигая излом выноски.
/// </summary>
internal sealed class AlignmentPass(LayoutScene scene, PlacementScorer scorer)
{
    private readonly LayoutSettings _s = scene.Settings;
    private readonly List<int> _buffer = new(64);

    public int Moves { get; private set; }

    public void Run(LayoutState state, Func<bool> cancelled)
    {
        for (var pass = 0; pass < 2; pass++)
        for (var index = 0; index < scene.Items.Length; index++)
        {
            if (cancelled()) return;
            if (state[index] is not { Stacked: false } placed) continue;

            var item = scene.Items[index];
            var bestScore = scorer.Full(item, placed, state);
            if (bestScore.Hard > 0) continue;

            Placement? best = null;
            _buffer.Clear();
            state.QueryNeighbors(placed.Bounds.Inflate(scorer.AlignRadius), _buffer);
            foreach (var other in _buffer)
            {
                if (other == index || state[other] is not { } neighbor || neighbor.Side != placed.Side) continue;

                var dx = neighbor.AlignEdge - placed.AlignEdge;
                if (Math.Abs(dx) < 0.05 || Math.Abs(dx) > _s.AlignSnap) continue;

                var text = placed.Text.Shift(dx, 0);

                // Вариант 1: излом на месте, меняется длина полки.
                var shelf = placed.Side == ShelfSide.Right ? placed.ShelfLength + dx : placed.ShelfLength - dx;
                if (shelf >= _s.MinShelf && shelf <= _s.MaxShelf)
                    Consider(item, Placement.FromText(item, placed.AnchorIndex, text, placed.Side, shelf), state,
                        ref best, ref bestScore);

                // Вариант 2: полка прежняя, сдвигается излом.
                var shifted = Placement.FromText(item, placed.AnchorIndex, text, placed.Side, placed.ShelfLength);
                if (shifted.LeaderLength >= _s.MinLeader * 0.8 && shifted.LeaderLength <= _s.MaxLeader)
                    Consider(item, shifted, state, ref best, ref bestScore);
            }

            if (best is null) continue;

            state.Set(index, best);
            Moves++;
        }
    }

    private void Consider(LayoutItem item, Placement candidate, LayoutState state, ref Placement? best,
        ref Score bestScore)
    {
        var score = scorer.Full(item, candidate, state);
        if (score.Hard != 0 || !score.IsBetterThan(bestScore)) return;

        bestScore = score;
        best = candidate;
    }
}
