using System.IO;
using System.Windows;
using Microsoft.Win32;
using ParamChecker.Services;
using ParamChecker.ViewModels;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace ParamChecker.Views;

/// <summary>Окна и диалоги ParamChecker; все открываются поверх главного окна.</summary>
public sealed class WindowService : IWindowService
{
    private const string Title = "ParamChecker";

    public Window? Owner { get; set; }

    public bool EditFilter(FilterEditorViewModel editor) => new FilterEditorWindow(editor) { Owner = Owner }.ShowDialog() == true;

    public bool EditParameters(ParametersEditorViewModel editor) => new ParametersWindow(editor) { Owner = Owner }.ShowDialog() == true;

    public bool PickServerModels(ServerPickerViewModel picker) => new ServerPickerWindow(picker) { Owner = Owner }.ShowDialog() == true;

    public IReadOnlyList<string> PickModelFiles()
    {
        var dialog = new OpenFileDialog { Title = "Модели для проверки", Filter = "Модели Revit (*.rvt)|*.rvt", Multiselect = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : [];
    }

    public string? PickFolder(string initial, string title) => FolderPicker.PickFolder(initial, title);

    public string? PickSaveFile(string title, string filter, string fileName, string? folder)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName };
        if (folder is not null && Directory.Exists(folder)) dialog.InitialDirectory = folder;
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickOpenFile(string title, string filter, string? folder)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        if (folder is not null && Directory.Exists(folder)) dialog.InitialDirectory = folder;
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? Show(string message, DialogButtons buttons, DialogIcon icon) => ToadDialogService.Show(Title, message, buttons, icon, Owner);
}
