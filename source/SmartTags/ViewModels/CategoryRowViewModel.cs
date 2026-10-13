using SmartTags.Models;

namespace SmartTags.ViewModels;

/// <summary>Строка списка категорий: отметка и тип марки.</summary>
public sealed partial class CategoryRowViewModel(TaggableCategory category) : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private TagTypeOption? _selectedTagType = category.TagTypes.FirstOrDefault();

    public TaggableCategory Category { get; } = category;
    public string Title => $"{Category.Name}  ·  {Category.ElementCount}";
    public IReadOnlyList<TagTypeOption> TagTypes => Category.TagTypes;

    /// <summary>Строка пойдёт в запуск: отмечена, есть элементы и выбран тип марки.</summary>
    public bool IsReady => IsSelected && SelectedTagType is not null && Category.ElementCount > 0;
}
