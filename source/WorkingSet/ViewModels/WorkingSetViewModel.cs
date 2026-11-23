#nullable enable
using ToadDesign;
using System.Windows;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit;
using Microsoft.Win32;
using ToadTools.UI.Models;
using WorkingSet.Models;
using ToadTools.UI.Services;
using Settings = WorkingSet.Configuration.Settings;

namespace WorkingSet.ViewModels;

public partial class WorkingSetViewModel : ObservableObject
{
    private WorkingSetModel? _model;

    /// <summary>Почему таблица не прочиталась (нет диска, файл занят или не xlsx); null — прочиталась.</summary>
    private string? _readError;

    [ObservableProperty] private string? _selectedSection;
    [ObservableProperty] private List<string>? _sections;
    [ObservableProperty] private string? _excelFilePath;
    

    partial void OnExcelFilePathChanged(string? value)
    {
        _readError = null;
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
    /// <summary>
    ///     Листы таблицы — разделы. Таблица по умолчанию лежит на сетевом диске: вне сети, без доступа или с занятым файлом
    ///     окно всё равно открывается, а причина показывается, когда таблица понадобится.
    /// </summary>
    private void LoadSections()
    {
        try
        {
            Sections = _model?.GetSections();
        }
        catch (Exception exception)
        {
            Sections = null;
            _readError = ReadError(exception);
        }
    }

    private string ReadError(Exception exception) =>
        $"Не удалось прочитать таблицу рабочих наборов:\n{ExcelFilePath}\n\n{exception.Message}\n\n" +
        "Проверьте, что файл доступен и это книга Excel (.xlsx), или выберите другой файл.";

    [RelayCommand]
    private void CreateWorksets()
    {
        if (_readError is not null)
        {
            ToadDialogService.Show("Таблица не прочитана", _readError, DialogButtons.OK, DialogIcon.Warning);
            return;
        }

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

        List<string> names;
        try
        {
            names = _model.GetWorksetsFromSection(section);
        }
        catch (Exception exception)
        {
            ToadDialogService.Show("Таблица не прочитана", ReadError(exception), DialogButtons.OK, DialogIcon.Warning);
            return;
        }

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

        if (openFileDialog.ShowDialog() != true) return;

        ExcelFilePath = openFileDialog.FileName;
        if (_readError is not null)
            ToadDialogService.Show("Таблица не прочитана", _readError, DialogButtons.OK, DialogIcon.Warning);
    }

    public void LoadFromSettings(Settings settings)
    {
        ExcelFilePath = settings.ExcelFilePath;
    }

    public Settings ToSettings()
    {
        var settings = new Settings();
        if (ExcelFilePath != null)
            settings.ExcelFilePath = ExcelFilePath;

        return settings;
    }
}