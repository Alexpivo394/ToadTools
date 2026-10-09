using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI.Events; 

namespace WorksetCheck.Services;

public class FailureProcessorOpenDocument
{
    public void ApplicationOnFailuresProcessing(object? sender, FailuresProcessingEventArgs e)
    {
        var accessor = e.GetFailuresAccessor();
        accessor.DeleteAllWarnings();

        accessor.ResolveFailures(accessor.GetFailureMessages());

        var elementIds = accessor.GetFailureMessages()
            .SelectMany(item => item.GetFailingElementIds())
            .ToArray();

        if (elementIds.Length > 0)
        {
            accessor.DeleteElements(elementIds);
            e.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);
        }
        else
        {
            e.SetProcessingResult(FailureProcessingResult.Continue);
        }
    }

    public void UIApplicationOnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs e)
    {
        e.OverrideResult(1);
    }
}    