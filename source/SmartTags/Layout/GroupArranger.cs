using SmartTags.Layout.Geometry;

namespace SmartTags.Layout;

/// <summary>
///     Упорядочивает марки соседних элементов, как это делают руками на чертежах: тексты стопкой друг
///     под другом, кромки полок на одной вертикали, крутые параллельные выноски от элементов к концам полок.
///     Мешающие стопке чужие марки переставляются; вариант принимается, только если вместе с ними
///     не добавляет конфликтов и не намного хуже по мягким штрафам.
/// </summary>
internal sealed class GroupArranger(
    LayoutScene scene,
    PlacementScorer scorer,
    Func<int, LayoutState, Placement?>? relocate,
    Action<string>? log = null)
{
    // Больше марок в одной стопке не собираем — группа делится пополам.
    private const int MaxGroupSize = 8;

    // Штраф за выбор не основной точки привязки при подборе, мм.
    private const double AnchorRankCost = 1.5;

    // Трубы считаются параллельными, если угол между осями меньше ~11°.
    private const double ParallelCos = 0.98;

    private readonly LayoutSettings _s = scene.Settings;

    public int Arranged { get; private set; }

    public void Run(LayoutState state, Func<bool> cancelled)
    {
        foreach (var group in FindGroups(state))
        {
            if (cancelled()) return;
            Arrange(state, group, cancelled);
        }
    }

    /// <summary>
    ///     Группы соседних марок, от крупных к мелким: близкие точки привязки или параллельные
    ///     трубы/воздуховоды, идущие рядом (пара «газ — жидкость», соседние воздуховоды).
    /// </summary>
    private List<List<int>> FindGroups(LayoutState state)
    {
        var items = scene.Items;
        var radius = Math.Max(1, _s.ArrangeRadius);
        var sets = new DisjointSets(items.Length);
        var grid = new SpatialGrid(Math.Max(radius, 10));
        var placed = Enumerable.Range(0, items.Length)
            .Where(i => state[i] is not null && items[i].Anchors.Count > 0)
            .ToList();

        foreach (var i in placed)
            grid.Add(i, AnchorBounds(items[i]));

        var buffer = new List<int>();
        foreach (var i in placed)
        {
            buffer.Clear();
            grid.Query(AnchorBounds(items[i]).Inflate(radius), buffer);
            foreach (var other in buffer)
            {
                if (other > i && AreNeighbors(items[i], items[other], radius))
                    sets.Union(i, other);
            }
        }

        return placed
            .GroupBy(sets.Find)
            .Select(group => group.ToList())
            .Where(group => group.Count >= 2)
            .OrderByDescending(group => group.Count)
            .ToList();
    }

    private static bool AreNeighbors(LayoutItem a, LayoutItem b, double radius)
    {
        if (Vector2D.Distance(a.MainAnchor, b.MainAnchor) <= radius) return true;
        if (!a.IsLinear || !b.IsLinear) return false;

        var axis = a.Axis.Normalize();
        if (Math.Abs(Vector2D.Dot(axis, b.Axis.Normalize())) < ParallelCos) return false;

        // Расстояние между осями и перекрытие участков вдоль оси.
        var normal = axis.Perpendicular();
        if (Math.Abs(Vector2D.Dot(b.MainAnchor - a.MainAnchor, normal)) > radius) return false;

        Span(a, axis, out var a0, out var a1);
        Span(b, axis, out var b0, out var b1);
        return Math.Min(a1, b1) - Math.Max(a0, b0) > 0;
    }

    private static void Span(LayoutItem item, Vector2D axis, out double min, out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;
        foreach (var anchor in item.Anchors)
        {
            var t = Vector2D.Dot(anchor, axis);
            min = Math.Min(min, t);
            max = Math.Max(max, t);
        }
    }

    private static Box2D AnchorBounds(LayoutItem item)
    {
        var box = Box2D.Empty;
        foreach (var anchor in item.Anchors) box = box.Union(anchor);
        return box;
    }

    private void Arrange(LayoutState state, List<int> group, Func<bool> cancelled)
    {
        if (group.Count < 2 || cancelled()) return;
        if (group.Count <= MaxGroupSize && TryArrange(state, group)) return;

        // Целиком не выстроить — пробуем половинки.
        var (first, second) = Split(group);
        Arrange(state, first, cancelled);
        Arrange(state, second, cancelled);
    }

    /// <summary>Делит группу пополам поперёк её длинной стороны.</summary>
    private (List<int>, List<int>) Split(List<int> group)
    {
        var area = Box2D.Empty;
        foreach (var index in group) area = area.Union(scene.Items[index].MainAnchor);

        var byX = area.Width >= area.Height;
        var sorted = group
            .OrderBy(index => byX ? scene.Items[index].MainAnchor.X : scene.Items[index].MainAnchor.Y)
            .ToList();
        var half = sorted.Count / 2;
        return (sorted.GetRange(0, half), sorted.GetRange(half, sorted.Count - half));
    }

    private bool TryArrange(LayoutState state, List<int> group)
    {
        var current = group.Select(index => state[index]).ToArray();
        var currentScore = scorer.Group(state, group, current);

        // Сначала оцениваем стопки без чужих марок: мешающие соседние марки можно потом подвинуть.
        var shortlist = new List<(Placement?[] Stack, Score Score)>();
        foreach (var variant in AnchorSets(group).SelectMany(anchors => Variants(group, anchors)))
        {
            var score = scorer.Group(state, group, variant, outsiders: false);
            if (score.Hard > currentScore.Hard) continue;

            shortlist.Add((variant, score));
        }

        var verdict = "нет подходящей стопки";
        Placement?[]? best = null;
        var bestGain = double.MaxValue;
        foreach (var (stack, _) in shortlist.OrderBy(entry => entry.Score.Hard).ThenBy(entry => entry.Score.Soft).Take(Shortlist))
        {
            var evicted = Evictees(state, group, stack);
            if (evicted is null)
            {
                verdict = "мешают другие стопки";
                continue;
            }

            var area = group.Concat(evicted).ToList();
            var saved = area.Select(index => state[index]).ToArray();
            var before = scorer.Group(state, area, saved);

            Apply(state, group, stack, evicted);
            var after = scorer.Group(state, area, area.Select(index => state[index]).ToArray());
            for (var i = 0; i < area.Count; i++) state.Set(area[i], saved[i]);

            if (after.Hard > before.Hard)
            {
                verdict = "стопка даёт конфликты";
                continue;
            }

            var gain = after.Soft - before.Soft;
            if (after.Hard == before.Hard && gain > _s.ArrangeTolerance * group.Count)
            {
                verdict = "стопка заметно хуже";
                continue;
            }

            // Меньше конфликтов — безусловно лучше; иначе — по разнице мягких штрафов.
            gain += (after.Hard - before.Hard) * 1000;
            if (gain >= bestGain) continue;

            bestGain = gain;
            best = stack;
        }

        log?.Invoke($"Группа из {group.Count}: сейчас {currentScore}, вариантов {shortlist.Count}" +
                    (best is null ? $" — оставлено как есть ({verdict})" : $" — собрана ({bestGain:+0.##;-0.##;0})"));
        if (best is null) return false;

        Apply(state, group, best, Evictees(state, group, best)!);
        Arranged++;
        return true;
    }

    // Сколько лучших стопок пробовать с перестановкой соседей.
    private const int Shortlist = 4;

    /// <summary>
    ///     Чужие марки, с которыми стопка конфликтует. Null — если среди них есть уже собранная
    ///     стопка: её не двигаем.
    /// </summary>
    private List<int>? Evictees(LayoutState state, List<int> group, Placement?[] stack)
    {
        var members = new HashSet<int>(group);
        var result = new List<int>();
        for (var i = 0; i < group.Count; i++)
        {
            var placement = stack[i]!;
            var item = scene.Items[group[i]];
            _buffer.Clear();
            state.QueryNeighbors(placement.Bounds.Inflate(_s.SoftGap), _buffer);
            foreach (var other in _buffer)
            {
                if (members.Contains(other) || result.Contains(other) || state[other] is not { } placed) continue;
                if (scorer.Pair(item, placement, scene.Items[other], placed).Hard == 0) continue;
                if (placed.Stacked || relocate is null) return null;

                result.Add(other);
            }
        }

        return result;
    }

    /// <summary>Ставит стопку и переставляет вытесненные марки на лучшие свободные места.</summary>
    private void Apply(LayoutState state, List<int> group, Placement?[] stack, List<int> evicted)
    {
        for (var i = 0; i < group.Count; i++)
        {
            stack[i]!.Stacked = true;
            state.Set(group[i], stack[i]);
        }

        foreach (var index in evicted)
        {
            if (relocate!(index, state) is { } placement) state.Set(index, placement);
        }
    }

    private readonly List<int> _buffer = new(32);

    /// <summary>Точки привязки поближе к центру группы — чтобы выноски шли из одного места.</summary>
    private int[] PickAnchors(List<int> group)
    {
        var center = Vector2D.Zero;
        foreach (var index in group) center += scene.Items[index].MainAnchor;
        center *= 1.0 / group.Count;

        return group.Select(index =>
        {
            var item = scene.Items[index];
            var best = 0;
            var bestCost = double.MaxValue;
            for (var i = 0; i < item.Anchors.Count; i++)
            {
                var cost = Vector2D.Distance(item.Anchors[i], center) + AnchorRankCost * i;
                if (cost >= bestCost) continue;

                bestCost = cost;
                best = i;
            }

            return best;
        }).ToArray();
    }

    /// <summary>
    ///     Наборы точек привязки: поближе к центру группы, затем одинаковые по счёту точки вдоль труб —
    ///     у параллельных труб они стоят друг напротив друга, и стопку можно сдвинуть туда, где свободно.
    /// </summary>
    private IEnumerable<int[]> AnchorSets(List<int> group)
    {
        var central = PickAnchors(group);
        yield return central;

        var maxCount = group.Max(index => scene.Items[index].Anchors.Count);
        var seen = new HashSet<string> { string.Join(",", central) };
        for (var rank = 0; rank < maxCount; rank++)
        {
            var set = new int[group.Count];
            for (var i = 0; i < group.Count; i++)
            {
                var item = scene.Items[group[i]];
                set[i] = item.IsLinear && rank < item.Anchors.Count ? rank : central[i];
            }

            if (seen.Add(string.Join(",", set))) yield return set;
        }
    }

    private IEnumerable<Placement?[]> Variants(List<int> group, int[] anchors)
    {
        var angles = Angles();
        foreach (var side in new[] { ShelfSide.Left, ShelfSide.Right })
        foreach (var up in new[] { true, false })
        foreach (var offset in Offsets())
        foreach (var angle in angles)
        {
            if (Stack(group, anchors, side, up, offset, angle) is { } stack) yield return stack;
        }
    }

    /// <summary>
    ///     Стопка: концы полок на вертикали чуть сбоку от группы, тексты — дальше в ту же сторону.
    ///     Выноски идут вверх или вниз под углом <paramref name="angle" /> к горизонтали, параллельно
    ///     друг другу: порядок строк совпадает с порядком выносок, и они не перекрещиваются.
    /// </summary>
    private Placement?[]? Stack(List<int> group, int[] anchors, ShelfSide side, bool up, double offset, double angle)
    {
        var count = group.Count;
        var points = new Vector2D[count];
        var area = Box2D.Empty;
        for (var i = 0; i < count; i++)
        {
            points[i] = scene.Items[group[i]].Anchors[anchors[i]];
            area = area.Union(points[i]);
        }

        // Тексты слева — выноски приходят в правые концы полок, и наоборот.
        var elbowX = side == ShelfSide.Left ? area.MinX - offset : area.MaxX + offset;
        var slope = Math.Tan(angle * Math.PI / 180) * (up ? 1 : -1);

        var ideal = new double[count];
        for (var i = 0; i < count; i++)
            ideal[i] = points[i].Y + slope * Math.Abs(points[i].X - elbowX);

        var order = Enumerable.Range(0, count).OrderBy(i => ideal[i]).ToArray();
        var pitch = new double[count];
        for (var k = 0; k + 1 < count; k++)
        {
            var lower = scene.Items[group[order[k]]];
            var upper = scene.Items[group[order[k + 1]]];
            pitch[k] = (lower.TextHeight + upper.TextHeight) / 2 + Gap;
        }

        var ys = Pack(order.Select(i => ideal[i]).ToArray(), pitch);

        var result = new Placement?[count];
        for (var k = 0; k < count; k++)
        {
            var slot = order[k];
            var item = scene.Items[group[slot]];
            var y = ys[k];
            var edge = side == ShelfSide.Right ? elbowX + _s.Shelf : elbowX - _s.Shelf;
            var text = side == ShelfSide.Right
                ? new Box2D(edge, y - item.TextHeight / 2, edge + item.TextWidth, y + item.TextHeight / 2)
                : new Box2D(edge - item.TextWidth, y - item.TextHeight / 2, edge, y + item.TextHeight / 2);

            var placement = Placement.FromText(item, anchors[slot], text, side, _s.Shelf);
            if (!IsReasonable(item, placement, up)) return null;

            result[slot] = placement;
        }

        return result;
    }

    /// <summary>Выноска нужной длины, идёт в задуманную сторону, не круче вертикали и не вдоль своей трубы.</summary>
    private bool IsReasonable(LayoutItem item, Placement placement, bool up)
    {
        if (placement.LeaderLength < _s.MinLeader * 0.8 || placement.LeaderLength > _s.MaxLeader) return false;

        var direction = placement.Direction;
        if (up ? direction.Y <= 0 : direction.Y >= 0) return false;
        if (Math.Abs(direction.Y) < Math.Sin((MinStackAngle - 10) * Math.PI / 180)) return false;

        // Выноска уходит от текста — получилась бы полка-крючок.
        if (placement.Side == ShelfSide.Left ? direction.X > 0 : direction.X < 0) return false;

        return !item.IsLinear || Math.Abs(Vector2D.Dot(direction, item.Axis.Normalize())) <= 0.94;
    }

    // Положе 30° выноски в стопке не делаем: строки быстро разъезжаются.
    private const double MinStackAngle = 30;

    private const double SteepestAngle = 75;

    /// <summary>Зазор между соседними текстами стопки: строки идут плотно, как на чертежах.</summary>
    private double Gap => _s.MinGap * 1.1;

    /// <summary>Выбранные углы от 30°; если таких нет — 45°.</summary>
    private List<double> Angles()
    {
        var steep = _s.PreferredAngles.Where(angle => angle >= MinStackAngle && angle < 89).Distinct().ToList();
        if (steep.Count == 0)
            steep.Add(45);

        // Совсем крутая выноска поднимает стопку над теснотой, когда рядом места нет.
        if (_s.Quality != LayoutQuality.Fast && steep.Max() < SteepestAngle) steep.Add(SteepestAngle);
        return steep;
    }

    /// <summary>На сколько вертикаль концов полок отстоит от крайнего элемента группы.</summary>
    private List<double> Offsets()
    {
        var min = Math.Max(0.5, _s.MinLeader);
        var preferred = Math.Max(min, _s.PreferredLeader);
        var offsets = new List<double> { min * 0.5, min, preferred * 0.6 };
        if (_s.Quality == LayoutQuality.Fast) offsets.RemoveAt(2);
        return offsets;
    }

    /// <summary>
    ///     Плотная стопка с ровным шагом: строки идут подряд, вся стопка сдвинута так, чтобы
    ///     в среднем стоять на желаемых высотах. <paramref name="pitch" />[k] — шаг между строками k и k+1.
    /// </summary>
    internal static double[] Pack(double[] ideal, double[] pitch)
    {
        var count = ideal.Length;
        var offset = new double[count];
        for (var k = 1; k < count; k++) offset[k] = offset[k - 1] + pitch[k - 1];

        var baseValue = 0.0;
        for (var k = 0; k < count; k++) baseValue += ideal[k] - offset[k];
        baseValue /= Math.Max(1, count);

        var result = new double[count];
        for (var k = 0; k < count; k++) result[k] = baseValue + offset[k];
        return result;
    }
}
