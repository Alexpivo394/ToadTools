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
    private const int MaxGroupSize = 24;

    // Штраф за выбор не основной точки привязки при подборе, мм.
    private const double AnchorRankCost = 1.5;

    // Трубы считаются параллельными, если угол между осями меньше ~11°.
    private const double ParallelCos = 0.98;

    private readonly LayoutSettings _s = scene.Settings;

    public int Arranged { get; private set; }

    /// <summary>
    ///     Упорядочивает группы уже разложенных марок, если стопка не хуже разброса. Группы, собранные
    ///     посевом, не трогает.
    /// </summary>
    public void Run(LayoutState state, Func<bool> cancelled)
    {
        foreach (var group in FindGroups(state, placedOnly: true))
        {
            if (cancelled()) return;
            if (group.Any(index => state[index]!.Stacked)) continue;

            Arrange(state, group, cancelled);
        }
    }

    /// <summary>
    ///     Посев: до раскладки остальных марок ставит группы соседних марок стопками — пока место вокруг
    ///     свободно. В плотном месте иначе не получается: стопка, собранная поверх разложенных марок,
    ///     выталкивает десятки соседей. Стопка учитывает препятствия и уже посеянные стопки; если
    ///     даже лучшая заметно конфликтует, группа делится пополам.
    /// </summary>
    public void Seed(LayoutState state, Func<bool> cancelled)
    {
        foreach (var group in FindGroups(state, placedOnly: false))
        {
            if (cancelled()) return;
            SeedGroup(state, group, cancelled);
        }
    }

    private void SeedGroup(LayoutState state, List<int> group, Func<bool> cancelled)
    {
        if (group.Count < 2 || cancelled()) return;

        if (group.Count <= MaxGroupSize)
        {
            Placement?[]? best = null;
            var bestScore = Score.Worst;
            foreach (var variant in AnchorSets(group).SelectMany(anchors => Variants(group, anchors)))
            {
                var score = scorer.Group(state, group, variant);
                if (!score.IsBetterThan(bestScore)) continue;

                bestScore = score;
                best = variant;
            }

            // Стопка годится, если все подписи читаются: текст на элементе или выноска чуть длиннее
            // максимума в тесноте неизбежны, а наложения подписей, выноски через текст и крест-накрест — нет.
            var conflicts = best is null ? [] : Conflicts(state, group, best);
            var acceptable = best is not null && !conflicts.Any(c => PlacementScorer.Severity(c.Kind) > 1);
            log?.Invoke($"Посев группы из {group.Count}: лучшая стопка {(best is null ? "нет" : bestScore + " (" + Describe(conflicts) + ")")}" +
                        (acceptable ? " — поставлена" : " — делим"));
            if (acceptable)
            {
                Apply(state, group, best!, []);
                Arranged++;
                return;
            }
        }

        var (first, second) = Split(group);
        SeedGroup(state, first, cancelled);
        SeedGroup(state, second, cancelled);
    }

    private static string Describe(List<Conflict> conflicts) =>
        string.Join(", ", conflicts.GroupBy(c => c.Kind).Select(g => $"{g.Key}×{g.Count()}"));

    /// <summary>Жёсткие конфликты марок группы в положениях <paramref name="variant" />.</summary>
    private List<Conflict> Conflicts(LayoutState state, List<int> group, Placement?[] variant)
    {
        var saved = group.Select(index => state[index]).ToArray();
        for (var i = 0; i < group.Count; i++) state.Set(group[i], variant[i]);

        var conflicts = new List<Conflict>();
        foreach (var index in group)
        {
            if (state[index] is { } placement) scorer.Full(scene.Items[index], placement, state, conflicts);
        }

        for (var i = 0; i < group.Count; i++) state.Set(group[i], saved[i]);

        // Конфликт двух марок группы виден с обеих сторон — оставляем один.
        return conflicts
            .Where(c => c.IsHard)
            .GroupBy(c => (c.Kind, Math.Min(c.Item, c.OtherItem < 0 ? c.Item : c.OtherItem),
                Math.Max(c.Item, c.OtherItem), c.Obstacle))
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    ///     Группы соседних марок, от крупных к мелким: близкие точки привязки или параллельные
    ///     трубы/воздуховоды, идущие рядом (пара «газ — жидкость», соседние воздуховоды).
    /// </summary>
    private List<List<int>> FindGroups(LayoutState state, bool placedOnly)
    {
        var items = scene.Items;
        var radius = Math.Max(1, _s.ArrangeRadius);
        var sets = new DisjointSets(items.Length);
        var grid = new SpatialGrid(Math.Max(radius, 10));
        var placed = Enumerable.Range(0, items.Length)
            .Where(i => (!placedOnly || state[i] is not null) && items[i].Anchors.Count > 0 &&
                        items[i].TextWidth > 0 && items[i].TextHeight > 0)
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
        // Участки разных категорий (воздуховод над пучком труб) — разные стопки.
        if (a.IsLinear && b.IsLinear && a.Group != b.Group) return false;
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
        foreach (var (stack, score) in shortlist.OrderBy(entry => entry.Score.Hard).ThenBy(entry => entry.Score.Soft).Take(Shortlist))
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
                verdict = $"стопка даёт конфликты: своих {score.Hard}, с соседями было {before.Hard}, стало {after.Hard}, сдвинуто {evicted.Count}";
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

        // Веер можно сдвинуть вверх или вниз целиком — туда, где нет элементов.
        var height = group.Sum(index => scene.Items[index].TextHeight + Gap);
        double[] shifts = [0, height / 2, -height / 2, height, -height];
        foreach (var side in new[] { ShelfSide.Left, ShelfSide.Right })
        foreach (var offset in Offsets())
        {
            foreach (var mode in new[] { StackMode.Up, StackMode.Down })
            foreach (var angle in angles)
            {
                if (Stack(group, anchors, side, mode, offset, angle) is { } stack) yield return stack;
            }

            foreach (var shift in shifts)
            {
                if (Stack(group, anchors, side, StackMode.Fan, offset, 0, shift) is { } fan) yield return fan;
            }
        }

        // Большое скопление — два столбика по обе стороны: каждый вдвое короче, выноски тоже.
        if (group.Count < 4) yield break;

        foreach (var offset in Offsets())
        {
            if (TwoColumns(group, anchors, offset) is { } columns) yield return columns;
        }
    }

    /// <summary>Левая половина группы (по X точек привязки) — столбиком слева, правая — справа.</summary>
    private Placement?[]? TwoColumns(List<int> group, int[] anchors, double offset)
    {
        var bySide = Enumerable.Range(0, group.Count)
            .OrderBy(i => scene.Items[group[i]].Anchors[anchors[i]].X)
            .ThenBy(i => group[i])
            .ToArray();
        var half = bySide.Length / 2;
        var result = new Placement?[group.Count];
        foreach (var (slots, side) in new[] { (bySide.Take(half).ToArray(), ShelfSide.Left), (bySide.Skip(half).ToArray(), ShelfSide.Right) })
        {
            var members = slots.Select(slot => group[slot]).ToList();
            var points = slots.Select(slot => anchors[slot]).ToArray();
            if (Stack(members, points, side, StackMode.Fan, offset, 0) is not { } column) return null;

            for (var k = 0; k < slots.Length; k++) result[slots[k]] = column[k];
        }

        return result;
    }

    /// <summary>
    ///     Стопка: концы полок на вертикали чуть сбоку от группы, тексты — дальше в ту же сторону.
    ///     Выноски идут вверх или вниз под углом <paramref name="angle" /> к горизонтали, параллельно
    ///     друг другу: порядок строк совпадает с порядком выносок, и они не перекрещиваются.
    /// </summary>
    private Placement?[]? Stack(List<int> group, int[] anchors, ShelfSide side, StackMode mode, double offset, double angle,
        double shift = 0)
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
        var slope = mode == StackMode.Fan ? 0 : Math.Tan(angle * Math.PI / 180) * (mode == StackMode.Up ? 1 : -1);

        var ideal = new double[count];
        for (var i = 0; i < count; i++)
            ideal[i] = points[i].Y + slope * Math.Abs(points[i].X - elbowX);

        var order = Enumerable.Range(0, count).OrderBy(i => ideal[i]).ThenBy(i => points[i].X).ToArray();
        var ys = Rows(group, order, ideal, shift);

        // Выноски из разных точек в столбик могут перекреститься — меняем строки местами, пока
        // пересечения есть. Каждый обмен укорачивает выноски в сумме, так что перебор конечен.
        if (Untangle(points, order, ys, elbowX))
            ys = Rows(group, order, ideal, shift);

        // Стопка вверх или вниз — целиком по одну сторону от точек привязки: плотная стопка с центром
        // на желаемых высотах частью уходит за точки, и такие выноски шли бы не туда.
        if (mode != StackMode.Fan)
        {
            var rise = Math.Tan((MinStackAngle - 10) * Math.PI / 180);
            var correction = 0.0;
            for (var k = 0; k < count; k++)
            {
                var point = points[order[k]];
                var limit = point.Y + (mode == StackMode.Up ? 1 : -1) * Math.Abs(point.X - elbowX) * rise;
                correction = mode == StackMode.Up
                    ? Math.Max(correction, limit - ys[k] + 0.1)
                    : Math.Min(correction, limit - ys[k] - 0.1);
            }

            for (var k = 0; k < count; k++) ys[k] += correction;
        }

        var result = new Placement?[count];
        for (var k = 0; k < count; k++)
        {
            var slot = order[k];
            var item = scene.Items[group[slot]];
            var y = ys[k];
            var edge = side == ShelfSide.Right ? elbowX + _s.Shelf : elbowX - _s.Shelf;
            var text = Placement.TextAt(item, new Vector2D(edge, y), side);

            var placement = Placement.FromText(item, anchors[slot], text, side, _s.Shelf);
            if (!IsReasonable(item, placement, mode)) return null;

            result[slot] = placement;
        }

        return result;
    }

    /// <summary>Высоты полок строк в порядке <paramref name="order" />: плотно, в среднем на желаемых высотах, плюс сдвиг.</summary>
    private double[] Rows(List<int> group, int[] order, double[] ideal, double shift)
    {
        var count = order.Length;
        var pitch = new double[count];
        for (var k = 0; k + 1 < count; k++)
        {
            var lower = scene.Items[group[order[k]]];
            var upper = scene.Items[group[order[k + 1]]];
            // От полки нижней строки до верха её текста, зазор, затем от низа текста верхней до её полки.
            pitch[k] = lower.TextHeight - lower.ShelfHeight + Gap + upper.ShelfHeight;
        }

        var ys = Pack(order.Select(i => ideal[i]).ToArray(), pitch);
        for (var k = 0; k < count; k++) ys[k] += shift;
        return ys;
    }

    /// <summary>Меняет строки местами, пока выноски «точка → излом на вертикали» пересекаются.</summary>
    private static bool Untangle(Vector2D[] points, int[] order, double[] ys, double elbowX)
    {
        var changed = false;
        var count = order.Length;
        for (var guard = 0; guard < count * count; guard++)
        {
            var swapped = false;
            for (var a = 0; a < count && !swapped; a++)
            for (var b = a + 1; b < count && !swapped; b++)
            {
                var first = new Segment2D(points[order[a]], new Vector2D(elbowX, ys[a]));
                var second = new Segment2D(points[order[b]], new Vector2D(elbowX, ys[b]));
                if (!first.Crosses(second, 0.05)) continue;

                (order[a], order[b]) = (order[b], order[a]);
                swapped = true;
            }

            if (!swapped) break;
            changed = true;
        }

        return changed;
    }

    /// <summary>Выноска нужной длины, идёт в задуманную сторону, не круче вертикали и не вдоль своей трубы.</summary>
    private bool IsReasonable(LayoutItem item, Placement placement, StackMode mode)
    {
        var direction = placement.Direction;
        if (mode == StackMode.Fan)
        {
            // Веер: строки и выше, и ниже точки привязки. Выноски длиннее обычного — иначе большой
            // столбик не собрать, а длинная ровная выноска лучше подписей вразнобой.
            if (placement.LeaderLength < _s.MinLeader * 0.5 || placement.LeaderLength > _s.MaxLeader * FanStretch)
                return false;
        }
        else
        {
            if (placement.LeaderLength < _s.MinLeader * 0.8 || placement.LeaderLength > _s.MaxLeader * FanStretch) return false;
            if (mode == StackMode.Up ? direction.Y <= 0 : direction.Y >= 0) return false;
            if (Math.Abs(direction.Y) < Math.Sin((MinStackAngle - 10) * Math.PI / 180)) return false;
        }

        // Выноска уходит от текста — получилась бы полка-крючок.
        if (placement.Side == ShelfSide.Left ? direction.X > 0 : direction.X < 0) return false;

        return !item.IsLinear || Math.Abs(Vector2D.Dot(direction, item.Axis.Normalize())) <= 0.94;
    }

    // Во сколько раз выноска стопки может быть длиннее максимальной: большую стопку иначе не собрать,
    // а длинная ровная выноска лучше подписей вразнобой.
    private const double FanStretch = 1.5;

    private enum StackMode
    {
        Up,
        Down,

        /// <summary>Столбик по центру скопления, выноски расходятся веером вверх и вниз.</summary>
        Fan
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
