using Autodesk.Revit.Attributes;
using CreateSpaces.Views;
using Nice3point.Revit.Toolkit.External;

namespace ToadTools.Commands;

/// <summary>
///     Opens the "Создание пространств" window.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class CreateSpacesCommand : ToadCommand
{
    protected override void Run()
    {
        var view = Host.CreateScope<CreateSpacesView>();
        view.ShowDialog();
    }
}
