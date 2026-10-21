using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParamChecker.Core;
using ParamChecker.Core.Profiles;

namespace ParamChecker.ViewModels;

/// <summary>Модель в профиле. Пустые вид и ключевое слово — значит, как у профиля (подсказка в поле показывает, что именно).</summary>
public sealed partial class ModelItemViewModel : ObservableObject
{
    private readonly CheckerContext _context;
    private readonly ProfileViewModel _owner;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Name), nameof(IsServer))] private string _path;
    [ObservableProperty] private string _viewName;
    [ObservableProperty] private string _worksetKeyword;

    public ModelItemViewModel(CheckerContext context, ProfileViewModel owner, ModelSource model)
    {
        _context = context;
        _owner = owner;
        _path = model.Path;
        _viewName = model.ViewName ?? "";
        _worksetKeyword = model.WorksetKeyword ?? "";
    }

    public string Name => Path.Trim().Length == 0 ? "Новая модель" : ModelNames.FromPath(Path);

    public bool IsServer => ModelNames.IsServerPath(Path);

    /// <summary>Подсказка в пустом поле — что возьмётся из профиля; «=» отличает её от своего значения.</summary>
    public string ViewPlaceholder => _owner.ViewName.Trim().Length == 0 ? "= вся модель" : $"= {_owner.ViewName.Trim()}";

    public string WorksetPlaceholder => _owner.WorksetKeyword.Trim().Length == 0 ? "= все РН" : $"= {_owner.WorksetKeyword.Trim()}";

    public ModelSource ToModel() => new()
    {
        Path = Path.Trim(),
        ViewName = ViewName.Trim().Length == 0 ? null : ViewName.Trim(),
        WorksetKeyword = WorksetKeyword.Trim().Length == 0 ? null : WorksetKeyword.Trim()
    };

    internal void RefreshPlaceholders()
    {
        OnPropertyChanged(nameof(ViewPlaceholder));
        OnPropertyChanged(nameof(WorksetPlaceholder));
    }

    partial void OnPathChanged(string value) => _context.NotifyChanged();
    partial void OnViewNameChanged(string value) => _context.NotifyChanged();
    partial void OnWorksetKeywordChanged(string value) => _context.NotifyChanged();

    [RelayCommand]
    private void Remove() => _owner.RemoveModel(this);
}
