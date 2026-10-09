using Autodesk.Revit.Attributes;
using ClassifierCode.Views;
using Nice3point.Revit.Toolkit.External;

namespace ToadTools.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class ClassifierCodeCommand : ExternalCommand
{
    public override void Execute()
    {
        var view = Host.CreateScope<ClassifierCodeView>();
        view.ShowDialog();
    }
}
