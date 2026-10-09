using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using ParamChecker.Services;
using ParamChecker.ViewModels.PagesViewModels;
using ParamChecker.ViewModels.Windows;
using ParamCheckerView = ParamChecker.Views.Windows.ParamChecker;

namespace ToadTools.Commands;

/// <summary>
///     Exports a parameter-completeness report from the models on the server.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class ParamCheckerCommand : ExternalCommand
{
    public override void Execute()
    {
        var logger = new Logger();
        var doc = Application.ActiveUIDocument.Document;
        var categoryService = new CategoryService();
        categoryService.Initialize(doc);
        var setvm = new SettingsViewModel();
        var exportService = new ExportService(Application, categoryService, setvm, logger);
        var vm = new ParamCheckerViewModel(categoryService, exportService, setvm, logger);
        var view = new ParamCheckerView(vm, setvm);
        view.ShowDialog();
    }
}
