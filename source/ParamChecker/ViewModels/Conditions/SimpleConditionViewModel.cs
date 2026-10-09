using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParamChecker.Models.Filters;

namespace ParamChecker.ViewModels.Conditions;

public partial class SimpleConditionViewModel : ConditionViewModelBase
{
    [ObservableProperty] private string parameterName = string.Empty;

    [ObservableProperty] private FilterLogic selectedItemLogic;

    [ObservableProperty] private string value = string.Empty;

    public Action<SimpleConditionViewModel>? RemoveSimpleRequested { get; set; }

    public IEnumerable<FilterLogic> ItemsLogic =>
        Enum.GetValues(typeof(FilterLogic)).Cast<FilterLogic>();

    [RelayCommand]
    private void Remove()
    {
        RemoveSimpleRequested?.Invoke(this);
    }
}