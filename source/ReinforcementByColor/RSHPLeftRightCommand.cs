using Nice3point.Revit.Toolkit;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace ReinforcementByColor;

public class RSHPLeftRight
{
    public void Run()
    {
        var uiDoc = RevitContext.ActiveUiDocument!;
        var doc = uiDoc.Document;
        var view = doc.ActiveView;

        var map = new Dictionaries().Map;
        
        var step = new Dictionaries().Step;
        
        var selection = uiDoc.Selection.GetElementIds()
            .Select(elId => doc.GetElement(elId))
            .Where(el => el != null)
            .ToList();

        if (selection.Count == 0)
        {
            var dial1 = ToadDialogService.Show(
                "Ошибка!",
                "Не выбраны семейства для замены. Выберите семейства на виде и запустите команду повторно.",
                DialogButtons.OK,
                DialogIcon.Error
            );
            
            return;
        }
        
        var regions = selection
            .OfType<FilledRegion>()
            .ToList();


        var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_DetailComponents)
        .Cast<FamilySymbol>() 
            .ToList();

        int replaced = 0;

        using (var t = new Transaction(doc, "Replace FilledRegions"))
        {
            t.Start();

            foreach (var region in regions)
            {
                var type = doc.GetElement(region.GetTypeId());
                string regionName = type.Name;

                if (!map.TryGetValue(regionName, out var targetName)) continue;

                var targetFamilyName = "R-SHP-01 - Дополнительная";

                var symbol = symbols
                    .FirstOrDefault(x =>
                        x.Family.Name.Equals(targetFamilyName, StringComparison.OrdinalIgnoreCase) &&
                        x.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));

                if (symbol == null) continue;

                if (!symbol.IsActive) symbol.Activate();
                
                // start
                var loop = region.GetBoundaries().FirstOrDefault();
                if (loop == null)
                    continue;

                var lines = loop.OfType<Line>()
                    .Where(l => l.Length > 0.001)
                    .ToList();

                if (lines.Count < 4)
                    continue;
                
                var verticalLines = lines
                    .Where(l => Math.Abs(l.Direction.X) < 0.001)
                    .ToList();

                var horizontalLines = lines
                    .Where(l => Math.Abs(l.Direction.Y) < 0.001)
                    .ToList();

                if (!verticalLines.Any() || !horizontalLines.Any())
                    continue;
                

                var bottomLine = horizontalLines
                    .OrderBy(l => l.GetEndPoint(0).Y) 
                    .First();

                var p1 = bottomLine.GetEndPoint(0);
                var p2 = bottomLine.GetEndPoint(1);
                
                if (p1.X > p2.X)
                {
                    (p1, p2) = (p2, p1);
                }

                var horizontalLine = Line.CreateBound(p1, p2);
                

                var vertical = verticalLines
                    .OrderByDescending(l => l.Length)
                    .First();

                double length = vertical.Length;
                

                var instance = doc.Create.NewFamilyInstance(
                    horizontalLine,
                    symbol,
                    view);
                

                var param = instance.LookupParameter("Длина стержней");

                if (param != null && !param.IsReadOnly)
                {
                    param.Set(length);
                }
                // end
                
                if (step.TryGetValue(regionName, out double stepValue))
                {
                    var stepParam = instance.LookupParameter("REI.LNG.Шаг");

                    if (stepParam != null && !stepParam.IsReadOnly)
                    {
#if REVIT2021_OR_GREATER
                        double stepInFeet = UnitUtils.ConvertToInternalUnits(
                            stepValue,
                            UnitTypeId.Millimeters);
#else
                        double stepInFeet = UnitUtils.ConvertToInternalUnits(
                            stepValue,
                            DisplayUnitType.DUT_MILLIMETERS);
#endif

                        stepParam.Set(stepInFeet);
                    }
                }

                doc.Delete(region.Id);

                replaced++;
            }

            t.Commit();
        }
        
        
        var dial = ToadDialogService.Show(
            "Успех!",
            $"Заменено: {replaced}",
            DialogButtons.OK,
            DialogIcon.Info
        );

        return;
    }
    
}