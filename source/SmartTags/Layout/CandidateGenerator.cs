using SmartTags.Layout.Geometry;

namespace SmartTags.Layout;

/// <summary>Кандидат положения марки с заранее посчитанной «своей» оценкой.</summary>
internal sealed record Candidate(Placement Placement, Score Own);

/// <summary>
///     Перебирает положения марки: точки привязки × направления выноски × длины × сторона полки.
///     Оставляет лучшие по «своей» оценке, отсортированные от лучшего к худшему.
/// </summary>
internal sealed class CandidateGenerator(LayoutScene scene, PlacementScorer scorer)
{
    private readonly LayoutSettings _s = scene.Settings;

    public List<Candidate> Generate(LayoutItem item)
    {
        var candidates = new List<Candidate>();
        if (item.Anchors.Count == 0 || item.TextWidth <= 0 || item.TextHeight <= 0) return candidates;

        var anchorCount = Math.Min(item.Anchors.Count, _s.AnchorSamples);
        var directions = Directions(item);
        var lengths = Lengths();

        for (var anchor = 0; anchor < anchorCount; anchor++)
        foreach (var direction in directions)
        foreach (var length in lengths)
        foreach (var side in Sides(direction))
        {
            var placement = Placement.FromLeader(item, anchor, direction, length, side, _s.Shelf);
            candidates.Add(new Candidate(placement, scorer.Own(item, placement)));
        }

        candidates.Sort((a, b) => a.Own.Hard != b.Own.Hard
            ? a.Own.Hard.CompareTo(b.Own.Hard)
            : a.Own.Soft.CompareTo(b.Own.Soft));

        var limit = Math.Max(8, _s.MaxCandidatesPerItem);
        if (candidates.Count > limit)
            candidates.RemoveRange(limit, candidates.Count - limit);

        return candidates;
    }

    private List<Vector2D> Directions(LayoutItem item)
    {
        var raw = new List<Vector2D>();
        var axis = item.IsLinear ? item.Axis.Normalize() : Vector2D.Zero;

        if (item.IsLinear)
        {
            // Для трубы/воздуховода сначала — перпендикуляр и предпочтительные углы к оси.
            var normal = axis.Perpendicular();
            raw.Add(normal);
            raw.Add(-normal);
            foreach (var angle in _s.PreferredAngles)
            {
                var turn = 90 - angle;
                if (turn < 1e-6) continue;

                raw.Add(normal.Rotate(turn));
                raw.Add(normal.Rotate(-turn));
                raw.Add((-normal).Rotate(turn));
                raw.Add((-normal).Rotate(-turn));
            }
        }

        for (var angle = 0.0; angle < 360 - 1e-6; angle += _s.DirectionStep)
        {
            var direction = Vector2D.FromAngle(angle);
            // Выноска почти вдоль оси трубы ложится на саму трубу — не годится.
            if (!item.IsLinear || Math.Abs(Vector2D.Dot(direction, axis)) <= 0.94)
                raw.Add(direction);
        }

        var unique = new List<Vector2D>(raw.Count);
        foreach (var direction in raw)
        {
            if (unique.All(existing => Vector2D.Dot(existing, direction) <= 0.995))
                unique.Add(direction);
        }

        return unique;
    }

    private List<double> Lengths()
    {
        var min = Math.Max(0.5, _s.MinLeader);
        var preferred = Math.Max(min, _s.PreferredLeader);
        var max = Math.Max(preferred, _s.MaxLeader);

        var raw = new List<double> { min, preferred, Math.Min(max, preferred * 1.5), (preferred + max) / 2, max };
        if (_s.Quality != LayoutQuality.Fast)
        {
            raw.Add(Math.Max(min, preferred * 0.75));
            raw.Add(Math.Min(max, preferred * 2));
        }

        if (_s.Quality == LayoutQuality.Maximum)
        {
            raw.Add(Math.Min(max, preferred * 1.25));
            raw.Add(Math.Min(max, preferred * 2.5));
        }

        raw.Sort();
        var lengths = new List<double>(raw.Count);
        foreach (var length in raw)
        {
            if (lengths.Count == 0 || length - lengths[lengths.Count - 1] > 0.25)
                lengths.Add(length);
        }

        return lengths;
    }

    private IEnumerable<ShelfSide> Sides(Vector2D direction)
    {
        // Почти вертикальная выноска — полка в обе стороны, иначе — по ходу выноски.
        if (Math.Abs(direction.X) < 0.35 || _s.Quality == LayoutQuality.Maximum)
            return [ShelfSide.Right, ShelfSide.Left];

        return [direction.X >= 0 ? ShelfSide.Right : ShelfSide.Left];
    }
}
