using Alexpivo394.Revit.ParamCheck.Services;
using Autodesk.Revit.UI;
using ParamChecker.Services;

namespace ParamChecker.ViewModels;

/// <summary>Окна-редакторы, которые открывают модели представления. Реализуется в Views — там известен владелец окон.</summary>
public interface IWindowService
{
    bool EditFilter(FilterEditorViewModel editor);
    bool EditParameters(ParametersEditorViewModel editor);
    bool PickServerModels(ServerPickerViewModel picker);
    IReadOnlyList<string> PickModelFiles();
    string? PickFolder(string initial, string title);
    string? PickSaveFile(string title, string filter, string fileName, string? folder);
    string? PickOpenFile(string title, string filter, string? folder);

    /// <returns>Нажатая кнопка: OK, Yes, No, Cancel.</returns>
    string? Show(string message, ToadTools.UI.Models.DialogButtons buttons, ToadTools.UI.Models.DialogIcon icon);
}

/// <summary>Общее для всех моделей представления окна ParamChecker.</summary>
public sealed class CheckerContext(UIApplication application, CheckerSettings settings, CategoryService categories, IWindowService windows)
{
    private ParameterCatalog? _parameters;

    public UIApplication Application { get; } = application;
    public CheckerSettings Settings { get; } = settings;
    public CategoryService Categories { get; } = categories;
    public IWindowService Windows { get; } = windows;
    public string RevitYear => Application.Application.VersionNumber;

    /// <summary>Каталог имён параметров собирается при первом обращении — с системными параметрами это пара секунд.</summary>
    public ParameterCatalog Parameters =>
        _parameters ??= ParameterCatalog.Build(Application.Application, Application.ActiveUIDocument?.Document);

    /// <summary>Что-то в профилях изменилось — пора сохранить.</summary>
    public event EventHandler? Changed;

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
