using System.Diagnostics;
using System.Globalization;

namespace SmartTags.Layout;

/// <summary>
///     Раскладка марок без пересечений. Не зависит от Revit: на входе — точки привязки, размеры
///     текстов и препятствия в координатах листа, на выходе — положение выноски и текста каждой марки.
/// </summary>
public sealed class LayoutEngine(Action<string>? log = null)
{
    public LayoutResult Solve(LayoutRequest request, Action<LayoutProgress>? progress, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var scene = new LayoutScene(request);
        var settings = scene.Settings;
        var count = scene.Items.Length;
        var result = new LayoutResult(count);
        var stats = result.Stats;
        stats.Items = count;
        stats.Obstacles = scene.Obstacles.Length;
        stats.BudgetMs = settings.TimeBudgetMs(count);
        Func<bool> cancelled = () => token.IsCancellationRequested;

        Log("Раскладка: марок {0}, препятствий {1}, бюджет {2} мс, качество {3}",
            count, scene.Obstacles.Length, stats.BudgetMs, settings.Quality);
        if (count == 0) return result;

        var scorer = new PlacementScorer(scene);
        var state = new LayoutState(scene);

        // 1. Кандидаты положений для каждой марки.
        var generator = new CandidateGenerator(scene, scorer);
        var candidates = new List<Candidate>[count];
        for (var i = 0; i < count; i++)
        {
            candidates[i] = cancelled() ? [] : generator.Generate(scene.Items[i]);
            stats.Candidates += candidates[i].Count;
            if (i % 25 == 0 || i == count - 1)
                progress?.Invoke(new LayoutProgress("Варианты положений", i + 1, count));
        }

        Log("Кандидатов: {0}, {1} мс", stats.Candidates, clock.ElapsedMilliseconds);

        // 2. Жадная начальная раскладка.
        var optimizer = new LayoutOptimizer(scene, scorer, candidates, new Random(settings.Seed));
        optimizer.Greedy(state, step =>
        {
            if (step % 25 == 0 || step == count)
                progress?.Invoke(new LayoutProgress("Первичная раскладка", step, count));
        }, cancelled);
        var score = optimizer.Total(state, null);
        Log("Жадная раскладка: {0}, {1} мс", score, clock.ElapsedMilliseconds);

        // 3. Столбики для скоплений, которые жадный проход не развёл.
        var stacks = new StackBuilder(scene, scorer);
        if (!cancelled() && score.Hard > 0)
        {
            stacks.Run(state, cancelled);
            Log("Столбики: {0}, {1}", stacks.Built, optimizer.Total(state, null));
        }

        // 4. Отжиг и шлифовка в пределах бюджета времени.
        if (!cancelled())
        {
            var deadline = Math.Max(clock.ElapsedMilliseconds + 50, stats.BudgetMs);
            score = optimizer.Anneal(state, clock, deadline, progress, cancelled);
            Log("Отжиг: итераций {0}, {1}, {2} мс", optimizer.Iterations, score, clock.ElapsedMilliseconds);
        }

        if (!cancelled())
        {
            var deadline = Math.Max(clock.ElapsedMilliseconds + 100, stats.BudgetMs);
            var moves = optimizer.Polish(state, clock, deadline, cancelled);
            if (moves > 0) score = optimizer.Total(state, null);
            Log("Шлифовка: ходов {0}, {1}", moves, score);
        }

        if (!cancelled() && score.Hard > 0)
        {
            var before = stacks.Built;
            stacks.Run(state, cancelled);
            Log("Столбики (повтор): {0}, {1}", stacks.Built - before, optimizer.Total(state, null));
        }

        // 5. Выравнивание текстов в линию.
        var alignment = new AlignmentPass(scene, scorer);
        if (!cancelled())
        {
            alignment.Run(state, cancelled);
            Log("Выравнивание: ходов {0}", alignment.Moves);
        }

        Validate(scene, scorer, state, result);
        stats.Iterations = optimizer.Iterations;
        stats.Stacks = stacks.Built;
        stats.AlignMoves = alignment.Moves;
        stats.Cancelled = cancelled();
        stats.ElapsedMs = clock.ElapsedMilliseconds;

        Log("Итог: жёстких конфликтов {0}, {1} мс", stats.HardConflicts, stats.ElapsedMs);
        return result;
    }

    /// <summary>Итоговая проверка: статус каждой марки и список конфликтов без дублей.</summary>
    private static void Validate(LayoutScene scene, PlacementScorer scorer, LayoutState state, LayoutResult result)
    {
        var seen = new HashSet<(ConflictKind, int, int, int)>();
        var conflicts = new List<Conflict>();

        for (var i = 0; i < scene.Items.Length; i++)
        {
            var placed = state[i];
            result.Placements[i] = placed;
            if (placed is null)
            {
                result.Statuses[i] = PlacementStatus.NotPlaced;
                continue;
            }

            conflicts.Clear();
            scorer.Full(scene.Items[i], placed, state, conflicts);

            var hasHard = false;
            foreach (var conflict in conflicts)
            {
                hasHard |= conflict.IsHard;

                // Парный конфликт виден с обеих сторон — учитываем один раз.
                var key = conflict.OtherItem < 0
                    ? (conflict.Kind, conflict.Item, -1, conflict.Obstacle)
                    : (conflict.Kind, Math.Min(conflict.Item, conflict.OtherItem), Math.Max(conflict.Item, conflict.OtherItem), -1);
                if (!seen.Add(key)) continue;

                result.Conflicts.Add(conflict);
                if (conflict.IsHard) result.Stats.HardConflicts++;
            }

            result.Statuses[i] = hasHard ? PlacementStatus.PlacedWithConflicts : PlacementStatus.Placed;
        }
    }

    private void Log(string format, params object[] args)
    {
        log?.Invoke("[раскладка] " + string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
