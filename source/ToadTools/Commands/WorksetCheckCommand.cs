using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using WorksetCheck;

namespace ToadTools.Commands;

/// <summary>
///     Проверяет распределение элементов открытой модели по рабочим наборам и исправляет найденное.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class WorksetCheckCommand : ExternalCommand
{
    public override void Execute()
    {
        WorksetCheckLauncher.Run(Application);
    }
}
