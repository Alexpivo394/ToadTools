#nullable enable
using System.Windows;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using ToadTools.UI.Models;
using WorkingSet.Models;
using Wpf.Ui.Appearance;
using ToadTools.UI.Services;
using Settings = WorkingSet.Configuration.Settings;

namespace WorkingSet.ViewModels;

public partial class WorkingSetViewModel : ObservableObject
{
    private WorkingSetModel? _model;
    CreateWorksetsHandler? _createWorksetsHandler;
    private ExternalEvent? _externalEvent;

    [ObservableProperty] private string? _selectedSection;
    [ObservableProperty] private List<string>? _sections;
    [ObservableProperty] private bool _darkTheme = true;
    [ObservableProperty] private string? _excelFilePath;
    
    partial void OnDarkThemeChanged(bool value)
    {
        var newTheme = value ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeWatcherService.ApplyTheme(newTheme);
    }

    partial void OnExcelFilePathChanged(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _model = null;
            Sections = null;
            return;
        }

        _model = new WorkingSetModel(value!);
        _createWorksetsHandler = new CreateWorksetsHandler();
        _externalEvent = ExternalEvent.Create(_createWorksetsHandler);

        LoadSections();
    }

    public WorkingSetViewModel()
    {
    }
    private void LoadSections()
    {
        Sections = _model?.GetSections();
    }

    [RelayCommand]
    private void CreateWorksets()
    {
        if (SelectedSection is not { Length: > 0 } section)
        {
            string? dial1 = ToadDialogService.Show(
                "Внимание!",
                "Пожалуйста, выберите раздел.",
                DialogButtons.OK,
                DialogIcon.Warning
            );
            return;
        }
        
        if (_model == null || _createWorksetsHandler == null || _externalEvent == null)
        {
            ToadDialogService.Show(
                "Внимание!",
                "Пожалуйста, выберите Excel файл с рабочими наборами.",
                DialogButtons.OK,
                DialogIcon.Warning
            );
            return;
        }

        _createWorksetsHandler.Worksets = _model.GetWorksetsFromSection(section);
        _externalEvent.Raise();
        
        string? dial2 = ToadDialogService.Show(
            "Успех!",
            "Рабочие наборы созданы успешно!",
            DialogButtons.OK,
            DialogIcon.Warning
        );
        
    }
    
    [RelayCommand]
    private void SelectExcelFilePath(object parameter)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "Excel Files (*.xlsx; *.xls)|*.xlsx;*.xls|All files (*.*)|*.*",
            Title = "Выберите Excel файл",
            DefaultExt = ".xlsx",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };

        if (openFileDialog.ShowDialog() == true) ExcelFilePath = openFileDialog.FileName;
    }

    public void LoadFromSettings(Settings settings)
    {
        DarkTheme = settings.DarkTheme;
        ExcelFilePath = settings.ExcelFilePath;
    }

    public Settings ToSettings()
    {
        var settings = new Settings { DarkTheme = DarkTheme };
        if (ExcelFilePath != null)
            settings.ExcelFilePath = ExcelFilePath;

        return settings;
    }
}