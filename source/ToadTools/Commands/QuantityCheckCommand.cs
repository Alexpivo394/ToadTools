using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using QuantityCheck.Views;

namespace ToadTools.Commands;

/// <summary>
///     Opens the quantity-writing window.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class QuantityCheckCommand : ToadCommand
{
    protected override void Run()
    {
        var view = Host.CreateScope<QuantityCheckView>();
        view.ShowDialog();
    }
}
