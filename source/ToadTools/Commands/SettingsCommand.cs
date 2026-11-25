using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using ToadTools.Connection;
using ToadTools.UI.Services;

namespace ToadTools.Commands;

/// <summary>«Настройки» ToadTools: вход в ToadFlow, тема, компактный режим, журнал и диагностика для всех инструментов.</summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class SettingsCommand : ToadCommand
{
    protected override void Run()
    {
        SettingsDialog.Show(SettingsSection.ToadFlow, Application.MainWindowHandle);
    }
}
