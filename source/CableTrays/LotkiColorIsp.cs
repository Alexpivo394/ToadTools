//Command running revit application

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using CableTrays;
using Nice3point.Revit.Toolkit;

namespace LotkiColorIsp;

public class LotkiColorIspService
{
    private const string ParamIsp = "ADSK_Исполнение";

    public void Run() => TrayReport.Run("Покраска лотков по исполнению", Execute);

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

        //цвет и подпись для каждого значения ADSK_Исполнение
        var colors = new Dictionary<int, (string Name, OverrideGraphicSettings Ogs)>
        {
            [1] = ("Исполнение 1 — Сендзимир", TrayGraphics.Colored(255, 128, 192)),
            [2] = ("Исполнение 2 — горячее цинкование", TrayGraphics.Colored(0, 128, 192)),
            [3] = ("Исполнение 3 — нержавеющая сталь", TrayGraphics.Colored(0, 128, 0)),
        };
        var counts = colors.Keys.ToDictionary(k => k, _ => 0);
        var other = 0;
        var fittingsNoParam = 0;
        var traysNoParam = new List<string>();

        using (var t0 = new Transaction(doc, "Покраска лотков по исполнению"))
        {
            t0.Start();

            foreach (var element in alllotkirealall.Concat(allsoed))
            {
                var ispParam = element.LookupParameter(ParamIsp);

                if (ispParam != null && colors.TryGetValue(ispParam.AsInteger(), out var color))
                {
                    view.SetElementOverrides(element.Id, color.Ogs);
                    counts[ispParam.AsInteger()]++;
                    continue;
                }

                view.SetElementOverrides(element.Id, ogs4);
                if (ispParam != null)
                    other++;
                else if (element is CableTray)
                    traysNoParam.Add(TrayReport.Describe(element));
                else
                    fittingsNoParam++;
            }

            t0.Commit();
        }

        var report = new TrayReport($"Покрашено на виде «{view.Name}» (лотки и соединительные детали):");
        foreach (var pair in counts)
            report.Count(colors[pair.Key].Name, pair.Value);
        report.Count("Исполнение 0 или другое (чёрные)", other);
        report.Count($"Соединительные детали без «{ParamIsp}» (чёрные)", fittingsNoParam);
        report.Warning($"Лотки без параметра «{ParamIsp}», покрашены в чёрный", traysNoParam);
        report.Show();
    }
}
