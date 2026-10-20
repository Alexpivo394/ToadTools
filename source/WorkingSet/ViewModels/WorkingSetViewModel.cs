#nullable enable
using System.Windows;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit;
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
        
        if (_model == null)
        {
            ToadDialogService.Show(
                "Внимание!",
                "Пожалуйста, выберите Excel файл с рабочими наборами.",
                DialogButtons.OK,
                DialogIcon.Warning
            );
            return;
        }

        var doc = RevitContext.ActiveDocument!;
        if (!doc.IsWorkshared)
        {
            ToadDialogService.Show(
                "Внимание!",
                "В модели не включена совместная работа. Включите её (Совместная работа → Рабочие наборы) " +
                "и запустите плагин снова.",
                DialogButtons.OK,
                DialogIcon.Warning
            );
            return;
        }

        var names = _model.GetWorksetsFromSection(section);
        var created = WorksetCreator.Create(doc, names);

        ToadDialogService.Show(
            "Успех!",
            created > 0
                ? $"Создано рабочих наборов: {created} из {names.Count}."
                : $"Все {names.Count} рабочих наборов раздела «{section}» уже есть в модели.",
            DialogButtons.OK,
            DialogIcon.Info
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