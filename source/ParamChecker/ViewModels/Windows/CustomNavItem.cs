using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParamChecker.ViewModels.PagesViewModels;
using ParamChecker.Views.Dialogs;

namespace ParamChecker.ViewModels.Windows;

public partial class CustomNavItem : ObservableObject
{
    private readonly Action<CustomNavItem> _removeCallback;

    [ObservableProperty] private string? _title;
    [ObservableProperty] private bool _isChecked;

    public Action<Page>? OnNavigate;

    public CustomNavItem(bool isChecked, string? title, Page page, object viewModel, Action<CustomNavItem> removeCallback)
    {
        IsChecked = isChecked;
        Title = title;
        Page = page;
        ViewModelInstance = viewModel;
        _removeCallback = removeCallback;

        RemoveCommand = new RelayCommand(() => removeCallback?.Invoke(this));
        RenameCommand = new RelayCommand(() =>
        {
            var dialog = new Rename(Title);
            var result = dialog.ShowDialog();
            if (result == true) Title = dialog.Result;
        });
        SelectCommand = new RelayCommand(() => { OnNavigate?.Invoke(Page); });
    }
    
    partial void OnIsCheckedChanged(bool value)
    {
        if (ViewModelInstance is ExportProfilesViewModel vm)
        {
            vm.IsChecked = value;
        }
    }

    partial void OnTitleChanged(string? value)
    {
        if (ViewModelInstance is ExportProfilesViewModel vm)
        {
            vm.ProfileName = value;
        }
    }


    public Page Page { get; }

    public object ViewModelInstance { get; }

    public IRelayCommand RemoveCommand { get; }
    public IRelayCommand RenameCommand { get; }
    public IRelayCommand SelectCommand { get; }

}