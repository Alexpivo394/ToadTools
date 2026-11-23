using Autodesk.Revit.Attributes;
using Collisions;
using Nice3point.Revit.Toolkit.External;

namespace ToadTools.Commands;

/// <summary>Показывает или скрывает панель «Коллизии ToadFlow».</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class CollisionsCommand : ToadCommand
{
    protected override void Run()
    {
        CollisionsPane.Toggle(Application);
    }
}
