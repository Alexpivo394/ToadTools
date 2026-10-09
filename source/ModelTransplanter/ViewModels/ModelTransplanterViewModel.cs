using Autodesk.Revit.UI;
using System.Windows.Input;
using ModelTransplanter.Models;
using ModelTransplanter.Services;
using Nice3point.Revit.Toolkit;
using ToadTools.UI.Models;
using Wpf.Ui.Appearance;
using ToadTools.UI.Services;
using Settings = ModelTransplanter.Configuration.Settings;

namespace ModelTransplanter.ViewModels;

public partial class ModelTransplanterViewModel : ObservableObject
{
    private readonly UIApplication _uiApp;
    private Logger _logger;

    [ObservableProperty] private Document? _selectedSourceDoc;
    [ObservableProperty] private Document? _selectedTargetDoc;
    [ObservableProperty] private string _logFilePath;
    [ObservableProperty] private int _progressValue;
    [ObservableProperty] private bool _darkTheme = true;

    partial void OnDarkThemeChanged(bool value)
    {
        var newTheme = value ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeWatcherService.ApplyTheme(newTheme);
    }

    public List<Document> OpenDocuments { get; }
    public ModelTransplanterViewModel()
    {
        _uiApp = RevitContext.UiApplication!;
        OpenDocuments = _uiApp.Application.Documents.Cast<Document>().ToList();

        var logFilePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\TransferElementsLog.txt";
        _logger = new Logger(logFilePath);
        LogFilePath = logFilePath;
    }

    [RelayCommand]
    private void SelectLogPath(object parameter)
    {
        var saveFileDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Текстовые файлы (*.txt)|*.txt",
            DefaultExt = ".txt",
            FileName = "TransferElementsLog.txt"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            LogFilePath = saveFileDialog.FileName;
            _logger = new Logger(LogFilePath);
        }
    }
    
    [RelayCommand]
    private void TransferElements(object parameter)
    { 
        ProgressValue = 0;

        try
        {
            // Проверка документов
            if (SelectedSourceDoc == null || SelectedTargetDoc == null)
            {
                var dial = ToadDialogService.Show(
                    "Ошибка!",
                    "Не выбраны исходный или целевой документ",
                    DialogButtons.OK,
                    DialogIcon.Error
                );
                return;
            }

            if (SelectedSourceDoc.IsReadOnly || SelectedTargetDoc.IsReadOnly)
            {
                var dial = ToadDialogService.Show(
                    "Ошибка!",
                    "Один из документов открыт в режиме только для чтения",
                    DialogButtons.OK,
                    DialogIcon.Error
                );
                return;
            }

            try
            {
                var progress = new Progress<int>(value =>
                {
                    ProgressValue = value;
                });

                var transferService = new ElementTransferService(_logger);
                transferService.TransferElements(SelectedSourceDoc, SelectedTargetDoc, progress);

                var dial = ToadDialogService.Show(
                    "Успех!",
                    $"Перенос элементов завершен успешно!",
                    DialogButtons.OK,
                    DialogIcon.Info
                );
            }
            catch (Exception ex)
            {
                var dial = ToadDialogService.Show(
                    "Ошибка!",
                    $"Ошибка: {ex.Message}",
                    DialogButtons.OK,
                    DialogIcon.Error
                );
                _logger.LogError("Ошибка при переносе", ex);
            }
        }
        catch (Exception ex)
        {
            var dial = ToadDialogService.Show(
                "Ошибка!",
                $"Ошибка: {ex.Message}",
                DialogButtons.OK,
                DialogIcon.Error
            );
        }
    }

    public void LoadFromSettings(Settings settings)
    {
        DarkTheme = settings.IsDarkTheme;
        LogFilePath = settings.LogFilePath;
    }

    public Settings ToSettings()
    {
        return new Settings
        {
            IsDarkTheme = DarkTheme,
            LogFilePath = LogFilePath
        };
    }
}