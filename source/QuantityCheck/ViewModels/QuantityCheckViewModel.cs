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
        if (string.IsNullOrWhiteSpace(ParameterName))
        {
            ToadDialogService.Show("Внимание", "Введите имя параметра для записи количества.",
                DialogButtons.OK, DialogIcon.Warning);
            return;
        }

        (int Written, int Failed) result;
        try
        {
            result = _processor.Process(ParameterName!.Trim(), Reverse);
        }
        catch (OperationCanceledException)
        {
            ToadDialogService.Show("Прервано", "Запись количества прервана, изменения в модели откачены.",
                DialogButtons.OK, DialogIcon.Info);
            return;
        }

        var message = $"Количество записано: {result.Written}";
        if (result.Failed > 0)
            message += $"\nНе записано: {result.Failed} — причины в Документы\\QuantityCheckLog.txt";

        ToadDialogService.Show(
            "Готово",
            message,
            DialogButtons.OK,
            result.Failed > 0 ? DialogIcon.Warning : DialogIcon.Info
        );
    }
} 