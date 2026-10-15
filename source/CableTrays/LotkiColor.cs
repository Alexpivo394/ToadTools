//Command running revit application

using Autodesk.Revit.DB;
using CableTrays;
using Nice3point.Revit.Toolkit;

namespace LotkiColor;

public class LotkiColorService
{
    private const string ParamTypeName = "ADSK_Наименование (по типу)";

    public void Run() => TrayReport.Run("Покраска лотков по типу", Execute);

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

        var ogs3 = TrayGraphics.Black();

        //цвет для каждого значения ADSK_Наименование (по типу)
        var colors = new Dictionary<string, OverrideGraphicSettings>
        {
            ["Лоток перфорированный"] = TrayGraphics.Colored(0, 174, 152),
            ["Лоток неперфорированный"] = TrayGraphics.Colored(152, 76, 152),
            ["Лоток проволочный"] = TrayGraphics.Colored(255, 255, 0),
        };
        var counts = colors.Keys.ToDictionary(k => k, _ => 0);
        var other = 0;
        var noTypeName = new List<string>();

        using (var progress = TrayReport.StartProgress("Покраска лотков по типу"))
        using (var t0 = new Transaction(doc, "Покраска лотков по типу"))
        {
            t0.Start();

            foreach (var soed in progress.Track(allsoed, "Покраска соединительных деталей"))
                view.SetElementOverrides(soed.Id, ogs3);

            foreach (var lotok in progress.Track(alllotkirealall, "Покраска лотков"))
            {
                var typeName = doc.GetElement(lotok.GetTypeId())?.LookupParameter(ParamTypeName)?.AsString();

                if (typeName != null && colors.TryGetValue(typeName, out var ogs))
                {
                    view.SetElementOverrides(lotok.Id, ogs);
                    counts[typeName]++;
                }
                else
                {
                    view.SetElementOverrides(lotok.Id, ogs3);
                    if (string.IsNullOrEmpty(typeName))
                        noTypeName.Add(TrayReport.Describe(lotok));
                    else
                        other++;
                }
            }

            t0.Commit();
        }

        var report = new TrayReport($"Покрашено на виде «{view.Name}»:");
        foreach (var pair in counts)
            report.Count(pair.Key, pair.Value);
        report.Count("Другие типы лотков (чёрные)", other);
        report.Count("Соединительные детали (чёрные)", allsoed.Count);
        report.Warning($"Не заполнен «{ParamTypeName}», лотки покрашены в чёрный", noTypeName);
        report.Show();
    }
}
