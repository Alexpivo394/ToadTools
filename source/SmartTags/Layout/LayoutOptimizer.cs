using System.Diagnostics;
using SmartTags.Layout.Geometry;

namespace SmartTags.Layout;

/// <summary>
///     Подбор положений из кандидатов: жадная начальная раскладка, затем имитация отжига
///     (случайные перестановки, иногда принимаются и ухудшения) и финальная шлифовка.
/// </summary>
internal sealed class LayoutOptimizer(LayoutScene scene, PlacementScorer scorer, List<Candidate>[] candidates, Random random)
{
    // Вес жёсткого конфликта в «температурной» шкале отжига.
    private const double HardWeight = 8;
    private const double StartTemperature = 1.5;
    private const double EndTemperatureRatio = 0.02;

    private readonly List<int> _buffer = new(64);

    public int Iterations { get; private set; }

    /// <summary>
    ///     Жадная раскладка: сначала марки с приоритетом, потом самые «зажатые» (мало хороших кандидатов,
    ///     тесное место), каждой — лучший кандидат с учётом уже поставленных.
    /// </summary>
    public void Greedy(LayoutState state, Action<int>? progress, Func<bool> cancelled)
    {
        var items = scene.Items;
        var freeCandidates = new int[items.Length];
        var crowding = new double[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            freeCandidates[i] = candidates[i].Count(c => c.Own.Hard == 0);
            var reach = scene.Settings.MaxLeader * 2;
            crowding[i] = scene.Occupancy(Box2D.FromCenter(items[i].MainAnchor, reach, reach));
        }

        var order = Enumerable.Range(0, items.Length)
            .OrderByDescending(i => items[i].Priority)
            .ThenBy(i => freeCandidates[i])
            .ThenByDescending(i => crowding[i])
            .ThenBy(i => i)
            .ToArray();

        for (var step = 0; step < order.Length && !cancelled(); step++)
        {
            var item = order[step];
            // Уже стоит (посеянная стопка) — не трогаем.
            if (state[item] is not null)
            {
                progress?.Invoke(step + 1);
                continue;
            }

            if (BestCandidate(item, state, candidates[item].Count) is { } best)
                state.Set(item, best.Placement);

            progress?.Invoke(step + 1);
        }
    }

    /// <summary>Отжиг до дедлайна. Возвращает лучшую найденную оценку; состояние — лучшее найденное.</summary>
    public Score Anneal(LayoutState state, Stopwatch clock, long deadlineMs, Action<LayoutProgress>? progress,
        Func<bool> cancelled)
    {
        var count = scene.Items.Length;
        if (count == 0) return Score.Zero;

        var hardOf = new int[count];
        var current = Total(state, hardOf);
        var best = current;
        var bestState = state.Save();

        var start = clock.ElapsedMilliseconds;
        var duration = Math.Max(1.0, deadlineMs - start);
        var stallLimit = Math.Max(1500, 40 * count);
        var reportEvery = Math.Max(50, count);
        var stall = 0;
        var troubled = new List<int>();

        while (!cancelled() && clock.ElapsedMilliseconds < deadlineMs)
        {
            // Без конфликтов и долго без улучшений — дальше крутить бессмысленно.
            if (best.Hard == 0 && stall > stallLimit) break;

            Iterations++;
            var progressRatio = Math.Min(1.0, (clock.ElapsedMilliseconds - start) / duration);
            var temperature = StartTemperature * Math.Pow(EndTemperatureRatio, progressRatio);

            if ((Iterations & 63) == 1)
            {
                troubled.Clear();
                for (var i = 0; i < count; i++)
                {
                    if (hardOf[i] > 0 && candidates[i].Count > 0 && state[i] is not { Stacked: true }) troubled.Add(i);
                }
            }

            // В основном двигаем марки с конфликтами.
            var item = troubled.Count > 0 && random.NextDouble() < 0.75
                ? troubled[random.Next(troubled.Count)]
                : random.Next(count);

            var options = candidates[item];
            if (options.Count == 0 || state[item] is { Stacked: true })
            {
                stall++;
                continue;
            }

            var layoutItem = scene.Items[item];
            var placed = state[item];
            var before = placed is null ? Score.Zero : scorer.Full(layoutItem, placed, state);

            Candidate? move;
            Score after;
            if (hardOf[item] > 0 && random.NextDouble() < 0.08)
            {
                // Иногда для проблемной марки берём лучший ход из первых кандидатов.
                move = BestCandidate(item, state, Math.Min(options.Count, 48));
                if (move is null)
                {
                    stall++;
                    continue;
                }

                after = move.Own + scorer.Neighbors(layoutItem, move.Placement, state);
            }
            else
            {
                move = random.NextDouble() < 0.6
                    ? options[random.Next(Math.Min(options.Count, 12))]
                    : options[random.Next(options.Count)];
                after = move.Own + scorer.Neighbors(layoutItem, move.Placement, state);
            }

            if (ReferenceEquals(placed, move.Placement))
            {
                stall++;
                continue;
            }

            var delta = after - before;
            var accept = delta.Hard < 0 ||
                         (delta.Hard == 0 && delta.Soft <= 0) ||
                         random.NextDouble() < Math.Exp(-(delta.Hard * HardWeight + delta.Soft) / Math.Max(1e-6, temperature));
            if (!accept)
            {
                stall++;
                continue;
            }

            var affected = placed is null ? move.Placement.Bounds : move.Placement.Bounds.Union(placed.Bounds);
            state.Set(item, move.Placement);
            current += delta;
            RefreshHard(state, item, affected, hardOf);

            if (current.IsBetterThan(best))
            {
                best = current;
                bestState = state.Save();
                stall = 0;
            }
            else
            {
                stall++;
            }

            if (Iterations % reportEvery == 0)
                progress?.Invoke(new LayoutProgress("Оптимизация", (int)(progressRatio * 100), 100, best.Hard));
        }

        state.Restore(bestState);
        return Total(state, null);
    }

    /// <summary>
    ///     Шлифовка: каждой марке — лучший кандидат при неподвижных соседях, пока есть улучшения.
    ///     <paramref name="keepStacked" /> — не трогать марки, собранные в столбики и строки.
    /// </summary>
    public int Polish(LayoutState state, Stopwatch clock, long deadlineMs, Func<bool> cancelled, bool keepStacked = false)
    {
        var moves = 0;
        for (var pass = 0; pass < 4; pass++)
        {
            var improved = false;
            for (var item = 0; item < scene.Items.Length; item++)
            {
                if (clock.ElapsedMilliseconds >= deadlineMs || cancelled()) return moves;
                if (state[item] is not { } placed || candidates[item].Count == 0) continue;
                if (keepStacked && placed.Stacked) continue;

                var layoutItem = scene.Items[item];
                var bestScore = scorer.Full(layoutItem, placed, state);
                Candidate? best = null;
                foreach (var candidate in candidates[item])
                {
                    if (ReferenceEquals(candidate.Placement, placed) || candidate.Own.Hard > bestScore.Hard) continue;

                    var score = candidate.Own + scorer.Neighbors(layoutItem, candidate.Placement, state);
                    if (score.Hard < bestScore.Hard || (score.Hard == bestScore.Hard && bestScore.Soft - score.Soft > 1e-6))
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                if (best is null) continue;

                state.Set(item, best.Placement);
                moves++;
                improved = true;
            }

            if (!improved) break;
        }

        return moves;
    }

    /// <summary>
    ///     Спасение нечитаемых марок: тем, чей текст лежит на чужом тексте или под чужой выноской, добавляются
    ///     дальние кандидаты (выноска длиннее максимума), и марка переставляется на лучшее из всех мест.
    ///     Возвращает число перестановок.
    /// </summary>
    public int Rescue(LayoutState state, Func<LayoutItem, List<Candidate>> far, Stopwatch clock, long deadlineMs,
        Func<bool> cancelled)
    {
        var moves = 0;
        var extended = new bool[scene.Items.Length];
        var conflicts = new List<Conflict>();
        for (var pass = 0; pass < 3; pass++)
        {
            var improved = false;
            // Сначала самые нечитаемые: текст на тексте, потом перекрещенные выноски и так далее.
            foreach (var item in WorstFirst(state, conflicts))
            {
                if (clock.ElapsedMilliseconds >= deadlineMs || cancelled()) return moves;
                if (state[item] is not { } placed) continue;

                var layoutItem = scene.Items[item];
                conflicts.Clear();
                var current = scorer.Full(layoutItem, placed, state, conflicts);
                if (!conflicts.Any(c => c.IsHard && PlacementScorer.Severity(c.Kind) > 1)) continue;

                if (!extended[item])
                {
                    extended[item] = true;
                    candidates[item].AddRange(far(layoutItem));
                    candidates[item].Sort((a, b) => a.Own.Hard != b.Own.Hard
                        ? a.Own.Hard.CompareTo(b.Own.Hard)
                        : a.Own.Soft.CompareTo(b.Own.Soft));
                }

                if (BestCandidate(item, state, candidates[item].Count) is not { } best ||
                    ReferenceEquals(best.Placement, placed)) continue;

                var score = best.Own + scorer.Neighbors(layoutItem, best.Placement, state);
                if (!score.IsBetterThan(current)) continue;

                state.Set(item, best.Placement);
                moves++;
                improved = true;
            }

            if (!improved) break;
        }

        return moves;
    }

    /// <summary>Марки с жёсткими конфликтами тяжелее «выноска длиннее максимума», от самых тяжёлых.</summary>
    private List<int> WorstFirst(LayoutState state, List<Conflict> conflicts)
    {
        var worst = new int[scene.Items.Length];
        for (var item = 0; item < worst.Length; item++)
        {
            if (state[item] is not { } placed) continue;

            conflicts.Clear();
            scorer.Full(scene.Items[item], placed, state, conflicts);
            worst[item] = conflicts.Where(c => c.IsHard).Select(c => PlacementScorer.Severity(c.Kind)).DefaultIfEmpty(0).Max();
        }

        return Enumerable.Range(0, worst.Length)
            .Where(item => worst[item] > 1)
            .OrderByDescending(item => worst[item])
            .ToList();
    }

    /// <summary>Сумма оценок. Парные слагаемые входят в оценки обеих марок, поэтому делятся пополам.</summary>
    public Score Total(LayoutState state, int[]? hardOf)
    {
        var own = Score.Zero;
        var pairs = Score.Zero;
        for (var i = 0; i < scene.Items.Length; i++)
        {
            if (hardOf is not null) hardOf[i] = 0;
            if (state[i] is not { } placed) continue;

            var ownScore = scorer.Own(scene.Items[i], placed);
            var neighbors = scorer.Neighbors(scene.Items[i], placed, state);
            if (hardOf is not null) hardOf[i] = ownScore.Hard + neighbors.Hard;

            own += ownScore;
            pairs += neighbors;
        }

        return new Score(own.Hard + pairs.Hard / 2, own.Soft + pairs.Soft / 2);
    }

    /// <summary>Лучшее положение марки при неподвижных соседях.</summary>
    public Placement? Relocate(int item, LayoutState state)
    {
        return BestCandidate(item, state, candidates[item].Count)?.Placement;
    }

    private Candidate? BestCandidate(int item, LayoutState state, int limit)
    {
        var layoutItem = scene.Items[item];
        var options = candidates[item];
        Candidate? best = null;
        var bestScore = Score.Worst;

        for (var i = 0; i < limit; i++)
        {
            var candidate = options[i];
            // Кандидаты отсортированы по своей оценке: дальше будет только хуже.
            if (candidate.Own.Hard > bestScore.Hard) break;

            var score = candidate.Own + scorer.Neighbors(layoutItem, candidate.Placement, state);
            if (!score.IsBetterThan(bestScore)) continue;

            bestScore = score;
            best = candidate;
            if (score.IsPerfect) break;
        }

        return best;
    }

    private void RefreshHard(LayoutState state, int moved, Box2D affected, int[] hardOf)
    {
        _buffer.Clear();
        state.QueryNeighbors(affected.Inflate(scene.Settings.SoftGap), _buffer);
        if (!_buffer.Contains(moved)) _buffer.Add(moved);

        foreach (var item in _buffer)
        {
            hardOf[item] = state[item] is { } placed ? scorer.Full(scene.Items[item], placed, state).Hard : 0;
        }
    }
}
