using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using SSPlan.Services;
using SSPlan.ViewModels;
using SSPlan.Views;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace ToadTools.Commands;

/// <summary>
///     Builds a structural (single-line) diagram on the active drafting view.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class SSPlanCommand : ToadCommand
{
    protected override void Run()
    {
        var uiDoc = Application.ActiveUIDocument;
        var doc = Application.ActiveUIDocument.Document;
        var viewService = new ViewService(uiDoc);

        var activeView = viewService.GetActiveDraftingOrSheetView();

        if (activeView == null)
        {
            ToadDialogService.Show(
                "Ошибка",
                "Активный вид не является чертежным видом",
                DialogButtons.OK,
                DialogIcon.Error
            );
            return;
        }

        var vm = new SSPlanViewModel(doc, uiDoc, activeView);
        vm.LoadPanels();
        vm.LoadFamilies();

        var view = new SSPlanView(vm);

        var windowService = new SSPlanWindowService(view);
        vm.SetWindowService(windowService);

        windowService.Show();
    }
}
