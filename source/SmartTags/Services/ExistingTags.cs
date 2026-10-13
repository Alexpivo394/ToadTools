using SmartTags.Layout.Geometry;
using SmartTags.Models;

namespace SmartTags.Services;

/// <summary>Марка, уже стоящая на виде.</summary>
internal sealed class ExistingTag(IndependentTag tag)
{
    public IndependentTag Tag { get; } = tag;
    public List<ElementId> TaggedIds { get; } = [];
    public XYZ? Head { get; set; }
    public XYZ? Elbow { get; set; }
    public XYZ? End { get; set; }
    public Box2D Text { get; set; } = Box2D.Empty;

    /// <summary>Марку забрала новая раскладка (режим «переразместить»).</summary>
    public bool Reused { get; set; }

    /// <summary>Марка будет удалена (режим «удалить и создать заново»).</summary>
    public bool Deleted { get; set; }

    /// <summary>Остальные марки — неподвижные препятствия.</summary>
    public bool IsObstacle => !Reused && !Deleted;
}

internal static class ExistingTags
{
    public static List<ExistingTag> Collect(TagView view, Action<string>? log)
    {
        var tags = new List<ExistingTag>();
        var tagElements = new FilteredElementCollector(view.Document, view.View.Id)
            .OfClass(typeof(IndependentTag))
            .Cast<IndependentTag>();

        foreach (var tag in tagElements)
        {
            var info = new ExistingTag(tag);
            try
            {
                if (tag.IsHidden(view.View)) continue;

                info.TaggedIds.AddRange(TagApi.TaggedElementIds(tag));
                info.Head = TagApi.GetHead(tag);
                info.End = TagApi.GetLeaderEnd(tag);
                info.Elbow = TagApi.GetLeaderElbow(tag);
                if (tag.get_BoundingBox(view.View) is { } box)
                    info.Text = view.Projection.Project(box);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Марка {RevitCompat.IdValue(tag.Id)}: {ex.Message}");
            }

            tags.Add(info);
        }

        log?.Invoke($"Марок на виде: {tags.Count}");
        return tags;
    }

    /// <summary>
    ///     Сопоставляет цели с уже стоящими марками по режиму: пропускает подписанные, забирает
    ///     их марку на переразмещение или помечает к удалению. Возвращает Id марок к удалению.
    /// </summary>
    public static List<ElementId> Apply(List<TagTarget> targets, List<ExistingTag> tags, ExistingTagMode mode,
        Action<string>? log)
    {
        var byElement = tags
            .SelectMany(tag => tag.TaggedIds.Select(id => (Id: id, Tag: tag)))
            .GroupBy(pair => pair.Id)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Tag).ToList());

        var toDelete = new List<ElementId>();
        var tagged = 0;
        foreach (var target in targets.Where(target => !target.IsSkipped))
        {
            var own = target.Elements
                .SelectMany(element => byElement.TryGetValue(element.Id, out var list) ? list : [])
                .Distinct()
                .ToList();
            if (own.Count == 0) continue;

            tagged++;
            switch (mode)
            {
                case ExistingTagMode.Skip:
                    target.MarkSkipped(SkipReason.AlreadyTagged);
                    break;
                case ExistingTagMode.Relayout:
                    // Марка на несколько элементов достаётся одной цели, остальные считаем подписанными.
                    var free = own.FirstOrDefault(tag => !tag.Reused);
                    if (free is null)
                    {
                        target.MarkSkipped(SkipReason.AlreadyTagged);
                        break;
                    }

                    free.Reused = true;
                    target.ExistingTag = free.Tag;
                    target.Tag = free.Tag;
                    break;
                case ExistingTagMode.Recreate:
                    foreach (var tag in own.Where(tag => !tag.Deleted))
                    {
                        tag.Deleted = true;
                        toDelete.Add(tag.Tag.Id);
                    }

                    break;
            }
        }

        log?.Invoke($"Уже подписано целей: {tagged}, режим {mode}, к удалению марок: {toDelete.Count}");
        return toDelete;
    }
}
