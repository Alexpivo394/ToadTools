using SmartTags.Layout.Geometry;
using SmartTags.Models;

namespace SmartTags.Services;

/// <summary>
///     Общая подпись: у соседних элементов с одинаковой маркой (решётки одного типоразмера и расхода,
///     одинаковые клапаны) текст ставится один раз, а выноски от всех элементов сходятся к нему.
///     Так оформляют схемы вручную: подпись не повторяется, на листе меньше текста.
///     Технически это отдельные марки с одним и тем же положением текста и излома.
/// </summary>
internal static class LabelSharing
{
    /// <param name="radius">Наибольшее расстояние между элементами общей подписи, мм на листе.</param>
    /// <param name="maxSize">Сколько элементов может быть у одной подписи.</param>
    public static void Apply(List<TagTarget> targets, ViewProjection projection, double radius, int maxSize,
        Action<string>? log)
    {
        var candidates = targets
            .Where(target => target is { IsSkipped: false, IsRun: false, ExistingTag: null, Measured: true, Anchors.Count: > 0 } &&
                             !string.IsNullOrWhiteSpace(target.Text))
            .ToList();

        int labels = 0, joined = 0;
        foreach (var same in candidates.GroupBy(target => (target.Choice.TagType.Id, target.Text)))
        {
            var pool = same.Select(target => (Target: target, Point: projection.ToSheet(target.Anchors[0]))).ToList();
            while (pool.Count > 1)
            {
                var group = Densest(pool, radius, maxSize);
                foreach (var member in group)
                    pool.Remove(member);
                if (group.Count < 2) continue;

                // Ведущая — ближайшая к середине группы: от её точки ищется место подписи.
                var center = new Vector2D(group.Average(member => member.Point.X), group.Average(member => member.Point.Y));
                var lead = group.OrderBy(member => Vector2D.Distance(member.Point, center)).First().Target;
                foreach (var member in group.Where(member => member.Target != lead))
                {
                    member.Target.SharedWith = lead;
                    lead.Sharing.Add(member.Target);
                    joined++;
                }

                labels++;
            }
        }

        log?.Invoke($"Общие подписи: {labels}, присоединено марок {joined}");
    }

    /// <summary>
    ///     Группа вокруг элемента с наибольшим числом соседей: ближайшие соседи добавляются, пока каждый
    ///     не дальше <paramref name="radius" /> от всех уже взятых.
    /// </summary>
    private static List<(TagTarget Target, Vector2D Point)> Densest(List<(TagTarget Target, Vector2D Point)> pool,
        double radius, int maxSize)
    {
        var seed = pool
            .OrderByDescending(candidate => pool.Count(other => Vector2D.Distance(candidate.Point, other.Point) <= radius))
            .First();

        var group = new List<(TagTarget Target, Vector2D Point)> { seed };
        foreach (var other in pool.Where(other => other.Target != seed.Target)
                     .OrderBy(other => Vector2D.Distance(seed.Point, other.Point)))
        {
            if (group.Count >= maxSize) break;
            if (group.All(member => Vector2D.Distance(member.Point, other.Point) <= radius))
                group.Add(other);
        }

        return group;
    }
}
