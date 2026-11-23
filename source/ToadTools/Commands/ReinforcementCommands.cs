using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using ReinforcementByColor;

namespace ToadTools.Commands;

/// <summary>
///     Replaces filled regions with R-SHP reinforcement aligned along Y.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class RSHPLeftRightCommand : ToadCommand
{
    protected override void Run() => new RSHPLeftRight().Run();
}

/// <summary>
///     Replaces filled regions with R-SHP reinforcement aligned along X.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class RSHPUpDownCommand : ToadCommand
{
    protected override void Run() => new RSHPUpDown().Run();
}

/// <summary>
///     Replaces filled regions with R-SUM reinforcement aligned along Y.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class RSUMLeftRightCommand : ToadCommand
{
    protected override void Run() => new RSUMLeftRight().Run();
}

/// <summary>
///     Replaces filled regions with R-SUM reinforcement aligned along X.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class RSUMUpDownCommand : ToadCommand
{
    protected override void Run() => new RSUMUpDown().Run();
}
