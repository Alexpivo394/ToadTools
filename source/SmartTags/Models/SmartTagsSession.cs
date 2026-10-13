using SmartTags.Services;

namespace SmartTags.Models;

/// <summary>Что найдено на виде к моменту открытия окна.</summary>
public sealed class SmartTagsSession(
    TagView view,
    IReadOnlyList<TaggableCategory> viewCategories,
    IReadOnlyList<TaggableCategory> selectionCategories,
    IReadOnlyCollection<ElementId> selectedIds)
{
    public TagView View { get; } = view;

    /// <summary>Категории всех видимых элементов вида.</summary>
    public IReadOnlyList<TaggableCategory> ViewCategories { get; } = viewCategories;

    /// <summary>Категории выбранных элементов (пусто — ничего подходящего не выбрано).</summary>
    public IReadOnlyList<TaggableCategory> SelectionCategories { get; } = selectionCategories;

    public IReadOnlyCollection<ElementId> SelectedIds { get; } = selectedIds;

    public bool HasSelection => SelectionCategories.Count > 0;
}
