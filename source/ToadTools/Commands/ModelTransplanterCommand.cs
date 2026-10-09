using Autodesk.Revit.Attributes;
using ModelTransplanter.Views;
using Nice3point.Revit.Toolkit.External;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace ToadTools.Commands;

/// <summary>
///     Opens the element-transplanter window.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class ModelTransplanterCommand : ExternalCommand
{
    public override void Execute()
    {
        if (Application.ActiveUIDocument?.Document.IsReadOnly == true)
        {
            ToadDialogService.Show(
                "Ошибка!",
                "Документ открыт в режиме только для чтения",
                DialogButtons.OK,
                DialogIcon.Error
            );
            return;
        }

        var view = Host.CreateScope<ModelTransplanterView>();
        view.ShowDialog();
    }
}
