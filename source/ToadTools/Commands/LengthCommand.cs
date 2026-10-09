using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Nice3point.Revit.Toolkit.External;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace ToadTools.Commands;

/// <summary>
///     Sums the length of the selected curve-based elements and reports it.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class LengthCommand : ExternalCommand
{
    public override void Execute()
    {
        var uiDocument = Application.ActiveUIDocument;
        var selection = uiDocument.Selection.GetElementIds()
            .Select(elId => uiDocument.Document.GetElement(elId))
            .ToList();

        double summ = 0;
        foreach (var element in selection)
        {
            var lengthParam = element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            if (lengthParam == null) continue;

            var length = lengthParam.AsDouble();
#if REVIT2021_OR_GREATER
            var reallength = UnitUtils.ConvertFromInternalUnits(length, UnitTypeId.Millimeters);
#else
            var reallength = UnitUtils.ConvertFromInternalUnits(length, DisplayUnitType.DUT_MILLIMETERS);
#endif
            summ += reallength;
        }

        summ = Math.Round(summ);

        ToadDialogService.Show(
            "Успех!",
            $"Длина элементов - {summ} мм",
            DialogButtons.OK,
            DialogIcon.Info
        );
    }
}
