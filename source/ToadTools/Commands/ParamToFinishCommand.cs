using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using ParamToFinish.Views;

namespace ToadTools.Commands;

/// <summary>
///     Opens the "Основание отделки" window.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class ParamToFinishCommand : ToadCommand
{
    protected override void Run()
    {
        var view = Host.CreateScope<ParamToFinishView>();
        view.ShowDialog();
    }
}
