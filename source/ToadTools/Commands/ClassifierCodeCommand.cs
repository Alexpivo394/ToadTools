using Autodesk.Revit.Attributes;
using ClassifierCode.Views;
using Nice3point.Revit.Toolkit.External;

namespace ToadTools.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class ClassifierCodeCommand : ToadCommand
{
    protected override void Run()
    {
        var view = Host.CreateScope<ClassifierCodeView>();
        view.ShowDialog();
    }
}
