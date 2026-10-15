//Command running revit application

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using CableTrays;
using Nice3point.Revit.Toolkit;

namespace LotkiColorKrshka;

public class LotkiColorKrshkaService
{
    private const string ParamCover = "ADSK_Крышка";

    public void Run() => TrayReport.Run("Покраска лотков с крышкой", Execute);

    private static void Execute()
    {
        var uidoc = RevitContext.ActiveUiDocument!;
        var doc = uidoc.Document;
        var view = doc.ActiveView;

        if (!TrayGraphics.CheckView(view)) return;

        var allsoed = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CableTrayFitting)
            .WhereElementIsNotElementType()
            .ToList();
        var alllotkirealall = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CableTray)
            .WhereElementIsNotElementType()
            .ToList();

        if (alllotkirealall.Count == 0 && allsoed.Count == 0)
        {
            TrayReport.ShowWarning("В модели нет кабельных лотков и соединительных деталей — красить нечего.");
            return;
        }

        var ogs4 = TrayGraphics.Black();
        var ogs1 = TrayGraphics.Colored(255, 0, 0);

        var withCover = 0;
        var withoutCover = 0;
        var fittingsNoParam = 0;
        var traysNoParam = new List<string>();

        using (var progress = TrayReport.StartProgress("Покраска лотков с крышкой"))
        using (var t = new Transaction(doc, "Покраска лотков с крышкой"))
        {
            t.Start();

            foreach (var element in progress.Track(alllotkirealall.Concat(allsoed).ToList(), "Покраска лотков и деталей"))
            {
                var param = element.LookupParameter(ParamCover);

                if (param is not null && param.AsInteger() == 1)
                {
                    view.SetElementOverrides(element.Id, ogs1);
                    withCover++;
                    continue;
                }

                view.SetElementOverrides(element.Id, ogs4);
                if (param is not null)
                    withoutCover++;
                else if (element is CableTray)
                    traysNoParam.Add(TrayReport.Describe(element));
                else
                    fittingsNoParam++;
            }

            t.Commit();
        }

        var report = new TrayReport($"Покрашено на виде «{view.Name}» (лотки и соединительные детали):");
        report.Count("С крышкой (красные)", withCover);
        report.Count("Без крышки (чёрные)", withoutCover);
        report.Count($"Соединительные детали без «{ParamCover}» (чёрные)", fittingsNoParam);
        report.Warning($"Лотки без параметра «{ParamCover}», покрашены в чёрный", traysNoParam);
        if (withCover == 0 && !report.HasWarnings)
            report.Line("\nЛотков с крышкой не найдено.");
        report.Show();
    }
}
