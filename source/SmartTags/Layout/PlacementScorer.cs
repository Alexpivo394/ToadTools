using SmartTags.Layout.Geometry;

namespace SmartTags.Layout;

/// <summary>
///     Оценивает положение марки. «Своя» оценка зависит только от марки и неподвижных препятствий
///     (кэшируется в <see cref="Placement" />), «соседская» — от положения других марок.
/// </summary>
internal sealed class PlacementScorer(LayoutScene scene)
{
    // Допуск при проверке пересечения отрезков: касание в пределах 0,05 мм не считается.
    private const double SegmentTolerance = 0.05;

    // Кромки текстов считаются выровненными, если расходятся не больше чем на 0,3 мм.
    private const double AlignTolerance = 0.3;

    // Выноска положе этого угла к горизонтали считается «плоской».
    private const double FlatAngle = 15;

    // Выноски соседних марок считаются параллельными, если расходятся меньше чем на ~10°.
    private const double ParallelCos = 0.985;

    private readonly LayoutSettings _s = scene.Settings;
    private readonly List<int> _buffer = new(64);
    private readonly List<int> _groupBuffer = new(64);
    private readonly List<int> _groupObstacles = new(64);
    private readonly HashSet<int> _counted = new();

    public double AlignRadius => scene.AlignRadius;

    public Score Full(LayoutItem item, Placement p, LayoutState state, List<Conflict>? conflicts = null)
    {
        return Own(item, p, conflicts) + Neighbors(item, p, state, conflicts);
    }

    public Score Own(LayoutItem item, Placement p, List<Conflict>? conflicts = null)
    {
        if (conflicts is null && p.OwnScore is { } cached) return cached;

        var score = ComputeOwn(item, p, conflicts);
        p.OwnScore = score;
        return score;
    }

    private Score ComputeOwn(LayoutItem item, Placement p, List<Conflict>? conflicts)
    {
        var hard = 0;
        var soft = _s.WeightAnchorRank * p.AnchorIndex;

        // Полка «против» наклона выноски выглядит как крючок.
        if (Math.Abs(p.Direction.X) >= 0.35 && p.Direction.X >= 0 != (p.Side == ShelfSide.Right))
            soft += _s.WeightReversedShelf;

        if (!scene.IsInsideBounds(p.Text))
            hard += Add(conflicts, ConflictKind.TextOutsideCrop, item.Id, -1, -1, true);

        if (p.LeaderLength > _s.MaxLeader + 1e-6)
            hard += Add(conflicts, ConflictKind.LeaderTooLong, item.Id, -1, -1, true);

        var preferred = Math.Max(0.1, _s.PreferredLeader);
        var deviation = (p.LeaderLength - preferred) / preferred;
        soft += deviation > 0
            ? _s.WeightLongLeader * deviation * deviation
            : _s.WeightShortLeader * deviation * deviation;

        soft += _s.WeightAnglePerDegree * AngleDeviation(p.Direction);

        // Почти горизонтальная выноска сливается с полкой и трубами — на чертежах так не делают.
        if (Geometry2D.Inclination(p.Direction) < FlatAngle) soft += _s.WeightFlatLeader;

        var textWithGap = p.Text.Inflate(_s.MinGap);
        var textWithSoftGap = p.Text.Inflate(_s.SoftGap);
        var leader = p.Leader;
        var shelf = p.Shelf;

        _buffer.Clear();
        scene.QueryObstacles(p.Bounds.Inflate(_s.SoftGap), _buffer);
        foreach (var index in _buffer)
        {
            var obstacle = scene.Obstacles[index];

            if (obstacle.Overlaps(textWithGap))
            {
                switch (obstacle.Kind)
                {
                    case ObstacleKind.Element when _s.AvoidTextOverElements:
                        hard += Add(conflicts, ConflictKind.TextOverElement, item.Id, -1, obstacle.Id, true);
                        break;
                    case ObstacleKind.Element:
                        soft += _s.WeightTextOverElement;
                        Add(conflicts, ConflictKind.TextOverElement, item.Id, -1, obstacle.Id, false);
                        break;
                    case ObstacleKind.TagText:
                    case ObstacleKind.Annotation:
                        hard += Add(conflicts, ConflictKind.TextOverTag, item.Id, -1, obstacle.Id, true);
                        break;
                    case ObstacleKind.TagLeader:
                        hard += Add(conflicts, ConflictKind.LeaderOverText, item.Id, -1, obstacle.Id, true);
                        break;
                }
            }
            else if (obstacle.Kind is ObstacleKind.TagText or ObstacleKind.Annotation &&
                     obstacle.Overlaps(textWithSoftGap))
            {
                soft += _s.WeightTooClose;
                Add(conflicts, ConflictKind.TooClose, item.Id, -1, obstacle.Id, false);
            }

            // Выноска может идти по своему же элементу.
            if (obstacle.OwnerItem == item.Id) continue;
            if (!obstacle.Hits(leader, SegmentTolerance) && !obstacle.Hits(shelf, SegmentTolerance)) continue;

            switch (obstacle.Kind)
            {
                case ObstacleKind.Element:
                    soft += _s.WeightLeaderOverElement;
                    Add(conflicts, ConflictKind.LeaderOverElement, item.Id, -1, obstacle.Id, false);
                    break;
                case ObstacleKind.TagText:
                case ObstacleKind.Annotation:
                    hard += Add(conflicts, ConflictKind.LeaderOverText, item.Id, -1, obstacle.Id, true);
                    break;
                case ObstacleKind.TagLeader:
                    hard += Add(conflicts, ConflictKind.LeaderOverTagLeader, item.Id, -1, obstacle.Id, true);
                    break;
            }
        }

        // Лёгкий штраф за тесноту: при прочих равных текст уходит на свободное место.
        soft += _s.WeightDensity * scene.Occupancy(p.Text.Inflate(_s.SoftGap * 2));

        return new Score(hard, soft);
    }

    /// <summary>Взаимная оценка двух марок: пересечения, выравнивание, согласованность сторон.</summary>
    public Score Pair(LayoutItem a, Placement pa, LayoutItem b, Placement pb, List<Conflict>? conflicts = null)
    {
        var score = Alignment(pa, pb) + Parallel(pa, pb);
        if (pa.Bounds.DistanceTo(pb.Bounds) <= _s.SoftGap + 1e-9)
            score += Collision(a, pa, b, pb, conflicts);

        if (pa.Side != pb.Side && scene.GroupNeighbors(a.Id).Contains(b.Id))
            score += new Score(0, _s.WeightSideMismatch / 2);

        return score;
    }

    private Score Collision(LayoutItem a, Placement pa, LayoutItem b, Placement pb, List<Conflict>? conflicts)
    {
        var hard = 0;
        var soft = 0.0;

        if (pa.Text.Inflate(_s.MinGap).Overlaps(pb.Text))
        {
            hard += Add(conflicts, ConflictKind.TextOverText, a.Id, b.Id, -1, true);
        }
        else if (pa.Text.Inflate(_s.SoftGap).Overlaps(pb.Text) && !InLine(pa, pb))
        {
            soft += _s.WeightTooClose;
            Add(conflicts, ConflictKind.TooClose, a.Id, b.Id, -1, false);
        }

        if (pa.Leader.Crosses(pb.Leader, SegmentTolerance) || pa.Leader.Crosses(pb.Shelf, SegmentTolerance) ||
            pa.Shelf.Crosses(pb.Leader, SegmentTolerance) || pa.Shelf.Crosses(pb.Shelf, SegmentTolerance))
            hard += Add(conflicts, ConflictKind.LeaderOverLeader, a.Id, b.Id, -1, true);

        if (pa.Leader.Hits(pb.Text) || pa.Shelf.Hits(pb.Text))
            hard += Add(conflicts, ConflictKind.LeaderOverText, a.Id, b.Id, -1, true);

        if (pb.Leader.Hits(pa.Text) || pb.Shelf.Hits(pa.Text))
            hard += Add(conflicts, ConflictKind.LeaderOverText, b.Id, a.Id, -1, true);

        if (Vector2D.Distance(pa.Anchor, pb.Anchor) < _s.MinGap * 2)
        {
            soft += _s.WeightSharedAnchor;
            Add(conflicts, ConflictKind.SharedAnchor, a.Id, b.Id, -1, false);
        }

        return new Score(hard, soft);
    }

    /// <summary>Бонус (отрицательный штраф) за тексты, выстроенные столбиком или в строку.</summary>
    private Score Alignment(Placement pa, Placement pb)
    {
        var gapX = pa.Text.GapX(pb.Text);
        var gapY = pa.Text.GapY(pb.Text);
        if (gapX >= AlignRadius || gapY >= AlignRadius) return Score.Zero;

        var bonus = 0.0;
        if (pa.Side == pb.Side && gapX <= 0 && Math.Abs(pa.AlignEdge - pb.AlignEdge) < AlignTolerance)
        {
            bonus += _s.WeightAlignment;
            // Ровный шаг столбика.
            if (gapY >= _s.MinGap - AlignTolerance && gapY <= _s.SoftGap + AlignTolerance)
                bonus += _s.WeightAlignment * 0.3;
        }
        else if (gapX > 0 && Math.Abs(pa.Text.Center.Y - pb.Text.Center.Y) < AlignTolerance)
        {
            bonus += _s.WeightAlignment * 0.5;
        }

        return new Score(0, -bonus);
    }

    /// <summary>
    ///     Бонус соседям с параллельными выносками в одну сторону: скопление марок читается как
    ///     упорядоченный веер, а не как «взрыв» во все стороны.
    /// </summary>
    private Score Parallel(Placement pa, Placement pb)
    {
        if (Vector2D.Distance(pa.Anchor, pb.Anchor) > AlignRadius) return Score.Zero;
        return Vector2D.Dot(pa.Direction, pb.Direction) >= ParallelCos && pa.Side == pb.Side
            ? new Score(0, -_s.WeightParallel)
            : Score.Zero;
    }

    /// <summary>Тексты стоят ровным столбиком или строкой — плотный шаг здесь задуман, а не «тесно».</summary>
    private static bool InLine(Placement pa, Placement pb)
    {
        return (pa.Side == pb.Side && Math.Abs(pa.AlignEdge - pb.AlignEdge) < AlignTolerance) ||
               Math.Abs(pa.Text.Center.Y - pb.Text.Center.Y) < AlignTolerance;
    }

    /// <summary>
    ///     Оценка группы марок в положениях <paramref name="placements" />: свои оценки, пары внутри группы
    ///     (каждая один раз) и, если <paramref name="outsiders" />, пары с марками вне группы.
    /// </summary>
    public Score Group(LayoutState state, IReadOnlyList<int> group, Placement?[] placements, bool outsiders = true)
    {
        var members = new HashSet<int>(group);
        var score = Score.Zero;
        for (var i = 0; i < group.Count; i++)
        {
            if (placements[i] is not { } placement) continue;

            var item = scene.Items[group[i]];
            score += Own(item, placement);
            score += new Score(0, -_s.WeightLeaderOverElement * LeaderOverMembers(item, placement, members));

            for (var j = i + 1; j < group.Count; j++)
            {
                if (placements[j] is { } other)
                    score += Pair(item, placement, scene.Items[group[j]], other);
            }

            if (!outsiders) continue;

            _groupBuffer.Clear();
            state.QueryNeighbors(placement.Bounds.Inflate(AlignRadius), _groupBuffer);
            foreach (var outsider in _groupBuffer)
            {
                if (!members.Contains(outsider) && state[outsider] is { } other)
                    score += Pair(item, placement, scene.Items[outsider], other);
            }
        }

        return score;
    }

    public Score Neighbors(LayoutItem item, Placement p, LayoutState state, List<Conflict>? conflicts = null)
    {
        var score = Score.Zero;
        _counted.Clear();

        // Ближние соседи — полная парная оценка.
        _buffer.Clear();
        state.QueryNeighbors(p.Bounds.Inflate(_s.SoftGap), _buffer);
        foreach (var other in _buffer)
        {
            if (other == item.Id || state[other] is not { } po) continue;

            _counted.Add(other);
            score += Pair(item, p, scene.Items[other], po, conflicts);
        }

        // Дальние соседи на одной вертикали (столбик) или горизонтали (строка) — только выравнивание.
        var edge = p.AlignEdge;
        _buffer.Clear();
        state.QueryNeighbors(new Box2D(edge - AlignTolerance, p.Text.MinY - AlignRadius,
            edge + AlignTolerance, p.Text.MaxY + AlignRadius), _buffer);
        var centerY = p.Text.Center.Y;
        state.QueryNeighbors(new Box2D(p.Text.MinX - AlignRadius, centerY - AlignTolerance,
            p.Text.MaxX + AlignRadius, centerY + AlignTolerance), _buffer);
        foreach (var other in _buffer)
        {
            if (other == item.Id || !_counted.Add(other) || state[other] is not { } po) continue;
            score += Alignment(p, po);
        }

        // Соседи по группе, стоящие по другую сторону.
        var mismatch = 0.0;
        foreach (var other in scene.GroupNeighbors(item.Id))
        {
            if (_counted.Contains(other) || state[other] is not { } po) continue;
            if (po.Side != p.Side) mismatch += _s.WeightSideMismatch / 2;
        }

        return score + new Score(0, mismatch);
    }

    /// <summary>
    ///     Сколько элементов других марок группы пересекает выноска. В стопке выноска к дальней трубе
    ///     неизбежно идёт через ближнюю — так рисуют и руками, штрафовать за это не нужно.
    /// </summary>
    private int LeaderOverMembers(LayoutItem item, Placement p, HashSet<int> members)
    {
        var count = 0;
        _groupObstacles.Clear();
        scene.QueryObstacles(p.Bounds.Inflate(_s.SoftGap), _groupObstacles);
        foreach (var index in _groupObstacles)
        {
            var obstacle = scene.Obstacles[index];
            if (obstacle.Kind != ObstacleKind.Element || obstacle.OwnerItem == item.Id ||
                !members.Contains(obstacle.OwnerItem)) continue;

            if (obstacle.Hits(p.Leader, SegmentTolerance) || obstacle.Hits(p.Shelf, SegmentTolerance)) count++;
        }

        return count;
    }

    private double AngleDeviation(Vector2D direction)
    {
        if (_s.PreferredAngles.Count == 0) return 0;

        var inclination = Geometry2D.Inclination(direction);
        return _s.PreferredAngles.Min(angle => Math.Abs(inclination - angle));
    }

    private static int Add(List<Conflict>? conflicts, ConflictKind kind, int item, int other, int obstacle, bool hard)
    {
        conflicts?.Add(new Conflict(kind, item, other, obstacle, hard));
        return hard ? 1 : 0;
    }
}
