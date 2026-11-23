using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using WriteDash.Views;

namespace ToadTools.Commands;

/// <summary>
///     Opens the "Записать прочерк" window.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class WriteDashCommand : ToadCommand
{
    protected override void Run()
    {
        var view = Host.CreateScope<WriteDashView>();
        view.ShowDialog();
    }
}
