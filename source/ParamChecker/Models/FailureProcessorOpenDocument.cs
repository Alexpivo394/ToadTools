using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace ParamChecker.Models;

public class FailureProcessorOpenDocument
{
    public void OnFailuresProcessing(object? sender, FailuresProcessingEventArgs e)
    {
        var accessor = e.GetFailuresAccessor();
        var failures = accessor.GetFailureMessages();

        if (failures.Count == 0)
        {
            e.SetProcessingResult(FailureProcessingResult.Continue);
            return;
        }
        

        foreach (var failure in failures)
        {
            var severity = failure.GetSeverity();

            if (severity == FailureSeverity.Warning)
            {
                // Молча сжираем предупреждения
                accessor.DeleteWarning(failure);
            }
            else
            {
                // Ошибки НЕ ТРОГАЕМ
            }
        }

        // ⚠️ КРИТИЧЕСКИЙ МОМЕНТ
        // Если есть ошибки — просто Continue
        // Revit сам решит, что делать
        e.SetProcessingResult(FailureProcessingResult.Continue);
    }

    public void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs e)
    {
        // ❗️ТОЛЬКО БЕЗОПАСНЫЕ ДИАЛОГИ
        switch (e.DialogId)
        {
            // Апгрейд версии
            case "Dialog_Revit_DocUpgrade":
                e.OverrideResult((int)TaskDialogResult.Ok);
                break;

            // Предупреждение о detached / central
            case "TaskDialog_DetachModel":
                e.OverrideResult((int)TaskDialogResult.Yes);
                break;

            // Всё остальное — НЕ ТРОГАЕМ
            default:
                e.OverrideResult(1001);
                break;
        }
    }
}