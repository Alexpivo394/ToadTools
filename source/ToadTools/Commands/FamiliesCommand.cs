using Autodesk.Revit.Attributes;
using Families;
using Nice3point.Revit.Toolkit.External;

namespace ToadTools.Commands;

/// <summary>Показывает или скрывает панель «Семейства ToadFlow».</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class FamiliesCommand : ExternalCommand
{
    public override void Execute()
    {
        FamiliesPane.Toggle(Application);
    }
}
