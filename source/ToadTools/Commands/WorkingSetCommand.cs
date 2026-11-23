using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using WorkingSet.Views;

namespace ToadTools.Commands;

/// <summary>
///     Opens the worksets-creation window.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class WorkingSetCommand : ToadCommand
{
    protected override void Run()
    {
        var view = Host.CreateScope<WorkingSetView>();
        view.ShowDialog();
    }
}
