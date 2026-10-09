using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParamChecker.Models.Filters;

namespace ParamChecker.ViewModels.Conditions;

public partial class GroupConditionViewModel : ConditionViewModelBase
{
    [ObservableProperty] private ObservableCollection<ConditionViewModelBase> children = new();

    [ObservableProperty] private FilterParameterLogic groupLogic;

    public Action<GroupConditionViewModel>? RemoveGroupRequested { get; set; }

    [RelayCommand]
    private void Add()
    {
        var simple = new SimpleConditionViewModel
        {
            RemoveSimpleRequested = vm => Children.Remove(vm)
        };
        Children.Add(simple);
    }

    [RelayCommand]
    private void Remove()
    {
        RemoveGroupRequested?.Invoke(this);
    }
}