using System.Collections.ObjectModel;
using Nice3point.Revit.Extensions.Runtime;
using Nice3point.Revit.Toolkit;
using ParamToFinish.Models;
using ParamToFinish.Services;
using ToadTools.UI.Models;
using ToadTools.UI.Services;
using Wpf.Ui.Appearance;

namespace ParamToFinish.ViewModels;

public sealed partial class ParamToFinishViewModel : ObservableObject
{
    [ObservableProperty] private bool _darkTheme = true;
    [ObservableProperty] private bool _allModel = false;
    [ObservableProperty] private string _filter = "";
    public event EventHandler? CloseRequested;

    [ObservableProperty] private ObservableCollection<ParameterDescriptor>? _wallParameters;
    [ObservableProperty] private ParameterDescriptor? _selectedWallParameter;
    [ObservableProperty] private ParameterDescriptor? _selectedFinishParameter;
    private readonly IFinishParameterTransferService _transferService;
    private readonly ParamToFinishSettingsService _settingsService;
    private readonly ParamToFinishSettings _settings;
    private bool _isRestoringSettings;

    public ParamToFinishViewModel(
        GetParameterService parameterService,
        IFinishParameterTransferService transferService,
        ParamToFinishSettingsService settingsService)
    {
        var wallParameters = parameterService.GetWallParameters(RevitContext.ActiveDocument!);
        WallParameters = new ObservableCollection<ParameterDescriptor>(wallParameters);
        _transferService = transferService;
        _settingsService = settingsService;
        _settings = _settingsService.Load();

        RestoreSettings();
    }
    
    partial void OnDarkThemeChanged(bool value)
    {
        var newTheme = value
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light;

        ThemeWatcherService.ApplyTheme(newTheme);
    }

    partial void OnFilterChanged(string value)
    {
        if (_isRestoringSettings)
            return;

        _settings.Filter = value;
        _settingsService.Save(_settings);
    }

    partial void OnSelectedWallParameterChanged(ParameterDescriptor? value)
    {
        if (_isRestoringSettings)
            return;

        _settings.WallParameterName = value?.Name;
        _settingsService.Save(_settings);
    }

    partial void OnSelectedFinishParameterChanged(ParameterDescriptor? value)
    {
        if (_isRestoringSettings)
            return;

        _settings.FinishParameterName = value?.Name;
        _settingsService.Save(_settings);
    }

    private void RestoreSettings()
    {
        _isRestoringSettings = true;

        Filter = _settings.Filter ?? string.Empty;
        SelectedWallParameter = FindParameter(_settings.WallParameterName);
        SelectedFinishParameter = FindParameter(_settings.FinishParameterName);

        _isRestoringSettings = false;
    }

    private ParameterDescriptor? FindParameter(string? parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
            return null;

        return WallParameters?.FirstOrDefault(x =>
            string.Equals(x.Name, parameterName, StringComparison.Ordinal));
    }

    [RelayCommand]
    private void Start()
    {
        if (SelectedWallParameter == null || SelectedFinishParameter == null)
        {
            var dial1 = ToadDialogService.Show(
                "Ошибка!",
                "Выберите параметры",
                DialogButtons.OK,
                DialogIcon.Error
            );
            
            return;
        }

        if (Filter.IsNullOrWhiteSpace())
        {
            var dial1 = ToadDialogService.Show(
                "Ошибка!",
                "Введите фильтр для поиска отделки",
                DialogButtons.OK,
                DialogIcon.Error
            );
            
            return;
        }
        
        try
        {
            _transferService.Transfer(SelectedWallParameter, SelectedFinishParameter, AllModel, Filter);
        }
        catch (OperationCanceledException)
        {
            ToadDialogService.Show("Прервано", "Запись основания прервана, изменения в модели откачены.",
                DialogButtons.OK, DialogIcon.Info);
            return;
        }

        var dial = ToadDialogService.Show(
            "Успех!",
            "Основание записано в отделку",
            DialogButtons.OK,
            DialogIcon.Info
        );

        if (dial == "OK")
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
