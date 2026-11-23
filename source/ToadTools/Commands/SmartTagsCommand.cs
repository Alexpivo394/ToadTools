using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using SmartTags;

namespace ToadTools.Commands;

/// <summary>
///     Расставляет марки на видимых элементах активного вида без пересечений текста и выносок.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class SmartTagsCommand : ToadCommand
{
    protected override void Run()
    {
        SmartTagsLauncher.Run(Application);
    }
}
