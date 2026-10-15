using Nice3point.Revit.Toolkit;
using QuantityCheck.Configuration;
using QuantityCheck.Models;
using QuantityCheck.Services;
using ToadTools.UI.Models;
using ToadTools.UI.Services;
using Wpf.Ui.Appearance;
using Settings = QuantityCheck.Configuration.Settings;

namespace QuantityCheck.ViewModels;

public partial class QuantityCheckViewModel(Logger? logger) : ObservableObject
{
    [ObservableProperty] private bool _darkTheme = true;
    [ObservableProperty] private string? _parameterName = "";
    [ObservableProperty] private double _reverse = 0;
    private readonly QuantityProcessor _processor = new QuantityProcessor(RevitContext.ActiveDocument!, logger);

    partial void OnDarkThemeChanged(bool value)
    {
        var newTheme = value ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeWatcherService.ApplyTheme(newTheme);
    }
    
    public void LoadFromSettings(Settings settings)
    {
        DarkTheme = settings.DarkTheme;
    }

    public Settings ToSettings()
    {
        return new Settings
        {
            DarkTheme = DarkTheme
        };
    }

    [RelayCommand]
    private void WriteQuantity()
    {
        try
        {
            _processor.Process(ParameterName, Reverse);
        }
        catch (OperationCanceledException)
        {
            ToadDialogService.Show("Прервано", "Запись количества прервана, изменения в модели откачены.",
                DialogButtons.OK, DialogIcon.Info);
            return;
        }

        
        string? dildo = ToadDialogService.Show(
            "Готово",
            "Длина заполнена",
            DialogButtons.OK,
            DialogIcon.Info
        );
    }
} 