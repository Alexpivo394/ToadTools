using Autodesk.Revit.Attributes;
using ChangeSharedFamilies.Services;
using Nice3point.Revit.Toolkit.External;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace ToadTools.Commands;

/// <summary>
///     Converts shared nested families of the current family document to non-shared.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class ChangeSharedFamiliesCommand : ExternalCommand
{
    public override void Execute()
    {
        var uiDoc = Application.ActiveUIDocument;

        if (uiDoc == null)
        {
            ErrorMessage = "Нет активного документа.";
            Result = Autodesk.Revit.UI.Result.Failed;
            return;
        }

        var doc = uiDoc.Document;

        if (!doc.IsFamilyDocument)
        {
            ToadDialogService.Show(
                "Ошибка",
                "Эту команду надо запускать в редакторе семейства, а не в проекте.",
                DialogButtons.OK,
                DialogIcon.Error);

            return;
        }

        try
        {
            var service = new CurrentFamilyNestedUnshareService(doc);

            var report = service.ConvertSharedNestedFamiliesToNonShared();

            var convertedNames = report.ConvertedFamilyNames.Count == 0
                ? "Ничего не найдено."
                : string.Join("\n", report.ConvertedFamilyNames);

            var errors = report.Errors.Count == 0
                ? ""
                : "\n\nОшибки:\n" + string.Join("\n", report.Errors);

            ToadDialogService.Show(
                "Готово",
                $"Текущее семейство: {doc.Title}\n" +
                $"Заменено shared вложенных семейств: {report.ConvertedFamiliesCount}\n\n" +
                convertedNames +
                errors,
                DialogButtons.OK,
                report.Errors.Any() ? DialogIcon.Warning : DialogIcon.Info);

            if (report.Errors.Any())
                Result = Autodesk.Revit.UI.Result.Failed;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;

            ToadDialogService.Show(
                "Ошибка",
                exception.ToString(),
                DialogButtons.OK,
                DialogIcon.Error);

            Result = Autodesk.Revit.UI.Result.Failed;
        }
    }
}
