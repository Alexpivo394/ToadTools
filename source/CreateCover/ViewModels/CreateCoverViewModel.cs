using System.Collections.ObjectModel;
using ToadTools.UI.Models;
using ToadTools.UI.Services;
using Wpf.Ui.Appearance;
using CreateCover.Models;
using CreateCover.Services;

namespace CreateCover.ViewModels;

public sealed partial class CreateCoverViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<ParamModel> _models = new();
    [ObservableProperty] private bool _darkTheme = true;
    private ParamModelCreator _paramCreator;
    private CreateCoverService _service;

    public CreateCoverViewModel(ParamModelCreator paramCreator, CreateCoverService service)
    {
        _paramCreator = paramCreator;
        _service = service;
    }
    partial void OnDarkThemeChanged(bool value)
    {
        var newTheme = value ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeWatcherService.ApplyTheme(newTheme);
    }
    
    [RelayCommand]
    private void AddModel()
    {
        var model = _paramCreator.Create();
        Models.Add(model);
    }

    [RelayCommand]
    private void RemoveModel(ParamModel model)
    {
        Models.Remove(model);
    }

    [RelayCommand]
    private void Start()
    {
        try
        {
            _service.Create(Models.ToList());
        }
        catch (OperationCanceledException)
        {
            ToadDialogService.Show("Прервано", "Размещение крышек прервано, изменения в модели откачены.",
                DialogButtons.OK, DialogIcon.Info);
        }
    }
}