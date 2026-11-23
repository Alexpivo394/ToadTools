using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using ParamChecker;

namespace ToadTools.Commands;

/// <summary>
///     Проверяет заполнение параметров в моделях с Revit Server, с диска или в открытой модели.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class ParamCheckerCommand : ToadCommand
{
    protected override void Run()
    {
        ParamCheckerLauncher.Run(Application);
    }
}
