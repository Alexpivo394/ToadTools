using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using ToadExporter;

namespace ToadTools.Commands;

/// <summary>
///     Выгружает модели с Revit Server или из папки в RVT (файл хранилища) и NWC.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class ToadExporterCommand : ExternalCommand
{
    public override void Execute()
    {
        ToadExporterLauncher.Run(Application);
    }
}
