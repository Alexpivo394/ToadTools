using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using ToadTools.Connection;

namespace ToadTools.Commands;

/// <summary>Вход в ToadFlow для всех плагинов ToadTools: ParamChecker, «Коллизии» и других.</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class ConnectionCommand : ToadCommand
{
    protected override void Run()
    {
        ConnectionDialog.Show(Application.MainWindowHandle);
    }
}
