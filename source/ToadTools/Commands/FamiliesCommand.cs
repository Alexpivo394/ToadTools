using Autodesk.Revit.Attributes;
using Families;
using Nice3point.Revit.Toolkit.External;

namespace ToadTools.Commands;

/// <summary>Показывает или скрывает панель «Семейства ToadFlow».</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class FamiliesCommand : ToadCommand
{
    protected override void Run()
    {
        FamiliesPane.Toggle(Application);
    }
}
