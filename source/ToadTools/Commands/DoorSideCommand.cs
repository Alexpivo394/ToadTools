using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using DoorSide;
using Nice3point.Revit.Toolkit.External;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace ToadTools.Commands;

/// <summary>
///     Writes the swing direction of every door in the active view into the "Открывание" parameter.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class DoorSideCommand : ExternalCommand
{
    private const string SwingParameterName = "Открывание";
    private static readonly Guid SwingParameterGuid = new("0F098FD7-5959-4FF6-AE0D-44E9CC4B7237");

    public override void Execute()
    {
        var doc = Application.ActiveUIDocument.Document;

        try
        {
            EnsureSwingSharedParameterExists(doc);

            var doors = GetDoorsInActiveView(doc);

            foreach (var door in doors)
            {
                var swing = DetectSwing(door);
                SetSwingParameter(doc, door, swing);
            }
        }
        catch (OperationCanceledException)
        {
            Result = Autodesk.Revit.UI.Result.Cancelled;
            return;
        }
        catch (Exception ex)
        {
            ToadDialogService.Show(
                "Ошибка!",
                $"Exception {ex.Message}\n{ex.StackTrace}",
                DialogButtons.OK,
                DialogIcon.Error
            );

            Result = Autodesk.Revit.UI.Result.Failed;
            return;
        }
        finally
        {
            ToadDialogService.Show(
                "Успех!",
                "Параметр открывание заполнен",
                DialogButtons.OK,
                DialogIcon.Info
            );
        }
    }

    private static void EnsureSwingSharedParameterExists(Document doc)
    {
        var spManager = new SharedParametersManager(doc);

        if (spManager.DoesParameterExist(SwingParameterName)) return;

        var categories = new List<BuiltInCategory> { BuiltInCategory.OST_Doors };

        spManager.CreateSharedParameter(
            parameterName: SwingParameterName,
#if REVIT2022_OR_GREATER
            parameterTypeId: SpecTypeId.String.Text,
#else
            parameterTypeId: ParameterType.Text,
#endif
            categories: categories,
#if REVIT2024_OR_GREATER
            parameterGroup: GroupTypeId.Data,
#else
            parameterGroup: BuiltInParameterGroup.PG_DATA,
#endif
            isInstance: true,
            isUserModifiable: true,
            isVisible: true,
            createTempFile: true,
            guid: SwingParameterGuid
        );

        spManager.SetAllowVaryBetweenGroups(SwingParameterName, true);
    }

    private static IList<FamilyInstance> GetDoorsInActiveView(Document doc)
    {
        return new FilteredElementCollector(doc, doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_Doors)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .ToList();
    }

    private static string DetectSwing(FamilyInstance door)
    {
        bool handFlipped = door.HandFlipped;
        bool facingFlipped = door.FacingFlipped;

        /*
         Логика:
         HandFlipped | FacingFlipped | Результат
         false       | false         | Левая
         true        | false         | Правая
         false       | true          | Правая
         true        | true          | Левая
        */

        if (!handFlipped && !facingFlipped) return "л";

        if (handFlipped && !facingFlipped) return "";

        if (!handFlipped && facingFlipped) return "";

        if (handFlipped && facingFlipped) return "л";

        return "N/A";
    }

    private static void SetSwingParameter(Document doc, FamilyInstance door, string value)
    {
        var param = door.LookupParameter(SwingParameterName);

        if (param == null || param.IsReadOnly) return;

        using var tx = new Transaction(doc, "Set door swing direction");
        tx.Start();
        param.Set(value);
        tx.Commit();
    }
}
