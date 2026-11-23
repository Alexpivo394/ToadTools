using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;

namespace ToadTools.Commands;

/// <summary>
///     Writes articles and names onto the selected (or all) cable trays.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class CableTrayArticulCommand : ToadCommand
{
    protected override void Run() => new ArticulLotok.ArticulService().Run();
}

/// <summary>
///     Colors cable trays by perforation type.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class CableTrayColorPerforationCommand : ToadCommand
{
    protected override void Run() => new LotkiColor.LotkiColorService().Run();
}

/// <summary>
///     Colors cable trays by finishing variant.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class CableTrayColorVariantCommand : ToadCommand
{
    protected override void Run() => new LotkiColorIsp.LotkiColorIspService().Run();
}

/// <summary>
///     Colors cable trays that carry a cover.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class CableTrayColorCoverCommand : ToadCommand
{
    protected override void Run() => new LotkiColorKrshka.LotkiColorKrshkaService().Run();
}
